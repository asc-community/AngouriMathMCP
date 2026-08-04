using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AngouriMath.Mcp;

// MCP stdio transport: newline-delimited JSON-RPC 2.0 on stdin/stdout. Nothing but protocol
// traffic may go to stdout — diagnostics belong on stderr or they corrupt the stream.
//
// Requests are handled strictly one at a time, and that is deliberate rather than lazy:
// MathS.Settings stores values in a process-global KeyStack with no thread affinity, so two
// concurrent calls with different parse settings would interfere. Serialising is the honest
// fix at this scale.

Console.OutputEncoding = new UTF8Encoding(false);
var stdout = Console.Out;

var serializerOptions = new JsonSerializerOptions { WriteIndented = false };

string? line;
while ((line = Console.In.ReadLine()) is not null)
{
    if (string.IsNullOrWhiteSpace(line)) continue;

    JsonObject? request;
    try
    {
        request = JsonNode.Parse(line) as JsonObject;
    }
    catch (JsonException e)
    {
        Send(Error(null, -32700, $"parse error: {e.Message}"));
        continue;
    }

    if (request is null) continue;

    var id = request.TryGetPropertyValue("id", out var idNode) ? idNode?.DeepClone() : null;
    var method = request.TryGetPropertyValue("method", out var m) ? m?.GetValue<string>() : null;
    var parameters = request.TryGetPropertyValue("params", out var p) ? p as JsonObject : null;

    if (method is null)
    {
        if (id is not null) Send(Error(id, -32600, "missing 'method'"));
        continue;
    }

    // Notifications carry no id and must never be answered.
    var isNotification = id is null;

    try
    {
        var result = Dispatch(method, parameters);
        if (result is null)
        {
            if (!isNotification) Send(Error(id, -32601, $"unknown method '{method}'"));
            continue;
        }
        if (!isNotification) Send(Ok(id, result));
    }
    catch (Exception e)
    {
        Console.Error.WriteLine($"[angourimath-mcp] {method} failed: {e}");
        if (!isNotification) Send(Error(id, -32603, $"{e.GetType().Name}: {e.Message}"));
    }
}

JsonObject? Dispatch(string method, JsonObject? parameters)
{
    switch (method)
    {
        case "initialize":
            return new JsonObject
            {
                // Pinned rather than echoed: this server implements exactly this revision.
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new JsonObject
                {
                    ["tools"] = new JsonObject(),
                    ["resources"] = new JsonObject(),
                    ["prompts"] = new JsonObject(),
                },
                ["serverInfo"] = new JsonObject
                {
                    ["name"] = "angourimath-mcp",
                    ["version"] = "0.1.0",
                },
                ["instructions"] =
                    "Exact symbolic algebra: simplify, solve, differentiate, integrate, " +
                    "limits, truth tables. Prefer these tools over doing algebra yourself — " +
                    "every answer is machine-checked and integrals are verified by " +
                    "differentiating them back. Always read the 'parsed' field to confirm " +
                    "the expression was understood as intended, and treat a 'declined' or " +
                    "'unchanged' status as 'no answer', never as the answer. Read the " +
                    "angourimath://syntax resource before composing unusual expressions.",
            };

        // Notifications. Acknowledged by returning an empty object; the caller suppresses
        // the response because there is no id.
        case "notifications/initialized":
        case "notifications/cancelled":
            return new JsonObject();

        case "ping":
            return new JsonObject();

        case "tools/list":
            return new JsonObject { ["tools"] = Tools.List() };

        case "tools/call":
        {
            var name = parameters?["name"]?.GetValue<string>();
            if (name is null) return ToolText("{\"status\":\"failed\",\"error\":\"missing tool name\"}", true);

            var arguments = parameters?["arguments"] as JsonObject ?? new JsonObject();

            JsonObject payload;
            try
            {
                payload = Tools.Call(name, arguments);
            }
            catch (Exception e)
            {
                // A thrown exception here is a defect in this server, not bad user input;
                // report it as a tool error rather than killing the connection.
                Console.Error.WriteLine($"[angourimath-mcp] tool {name} threw: {e}");
                payload = new JsonObject
                {
                    ["status"] = "failed",
                    ["error"] = $"{e.GetType().Name}: {e.Message}",
                };
            }

            var isError = payload["status"]?.GetValue<string>() is "failed";
            return ToolText(payload.ToJsonString(serializerOptions), isError);
        }

        case "prompts/list":
            return new JsonObject { ["prompts"] = Prompts.List() };

        case "prompts/get":
        {
            var promptName = parameters?["name"]?.GetValue<string>();
            if (promptName is null)
                throw new InvalidOperationException("prompts/get requires a name");

            var prompt = Prompts.Get(promptName, parameters?["arguments"] as JsonObject);
            if (prompt is null)
                throw new InvalidOperationException($"no such prompt: {promptName}");

            return prompt;
        }

        case "resources/list":
            return new JsonObject { ["resources"] = Resources.List() };

        case "resources/read":
        {
            var uri = parameters?["uri"]?.GetValue<string>();
            var text = uri is null ? null : Resources.Read(uri);
            if (text is null) throw new InvalidOperationException($"no such resource: {uri}");

            return new JsonObject
            {
                ["contents"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["uri"] = uri,
                        ["mimeType"] = "text/markdown",
                        ["text"] = text,
                    },
                },
            };
        }

        default:
            return null;
    }
}

static JsonObject ToolText(string text, bool isError) => new()
{
    ["content"] = new JsonArray
    {
        new JsonObject { ["type"] = "text", ["text"] = text },
    },
    ["isError"] = isError,
};

static JsonObject Ok(JsonNode? id, JsonObject result) => new()
{
    ["jsonrpc"] = "2.0",
    ["id"] = id,
    ["result"] = result,
};

static JsonObject Error(JsonNode? id, int code, string message) => new()
{
    ["jsonrpc"] = "2.0",
    ["id"] = id,
    ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
};

void Send(JsonObject message)
{
    stdout.WriteLine(message.ToJsonString(serializerOptions));
    stdout.Flush();
}
