using AngouriMath;

namespace AngouriMath.Mcp;

/// <summary>
/// Every AngouriMath call goes through here. Three protections, each of which exists
/// because of a documented failure in work/TRIAGE.md or work/coverage.md:
///
///  1. Cancellation via MathS.Multithreading.SetLocalCancellationToken — stops runaway
///     Simplify/Solve. Costs one line and is the library's own supported mechanism.
///  2. A dedicated 64 MB-stack thread, abandoned (not killed) on timeout. Cancellation
///     cannot rescue a stack overflow: on upstream master `integrate x*ln(x)` overflows
///     inside IntegrateByPartsPolynomial and takes the process with it. This is the same
///     trick work/casbench uses. .NET cannot abort a thread, so a hung case leaks one
///     thread for the life of the process — acceptable for a prototype, see README.
///  3. Result screening: unevaluated-node detection and a NaN screen (below).
/// </summary>
public static class Guard
{
    public const int DefaultTimeoutMs = 20_000;
    private const int StackBytes = 64 * 1024 * 1024;

    public readonly record struct Outcome<T>(T? Value, string Status, string? Error)
    {
        public bool Ok => Status == "ok";
    }

    public static Outcome<T> Run<T>(Func<T> work, int timeoutMs = DefaultTimeoutMs)
    {
        T? result = default;
        Exception? failure = null;
        using var cts = new CancellationTokenSource();

        var thread = new Thread(() =>
        {
            try
            {
                MathS.Multithreading.SetLocalCancellationToken(cts.Token);
                result = work();
            }
            catch (Exception e)
            {
                failure = e;
            }
        }, StackBytes) { IsBackground = true };

        thread.Start();

        if (!thread.Join(timeoutMs))
        {
            cts.Cancel();
            // Deliberately not joined again: the thread may be stuck in native/deep
            // recursion. Abandon it rather than block the server.
            return new Outcome<T>(default, "timeout",
                $"no answer within {timeoutMs} ms (the call was abandoned)");
        }

        if (failure is not null)
            return new Outcome<T>(default, "failed", Describe(failure));

        return new Outcome<T>(result, "ok", null);
    }

    private static string Describe(Exception e)
    {
        var inner = e is AggregateException agg && agg.InnerException is not null
            ? agg.InnerException
            : e;
        // AngouriBugException is an internal assertion (e.g. compiling a matrix, #425/#526).
        // Surfacing the type name tells the caller "library defect", not "bad input".
        return $"{inner.GetType().Name}: {inner.Message}";
    }

    /// <summary>
    /// Operators AngouriMath leaves in the tree when it cannot do the job. Finding one of
    /// these in the result means "declined", not "solved".
    ///
    /// This MUST be tested against the RAW result, before any Simplify: an unevaluated
    /// limit(...) simplifies to NaN, so simplifying first converts an honest decline into
    /// what looks like a wrong answer. work/casbench hit exactly this.
    /// </summary>
    private static readonly string[] UnevaluatedMarkers =
        ["integral(", "derivative(", "limit(", "limitleft(", "limitright("];

    public static bool IsDeclined(string rawStringized)
    {
        foreach (var marker in UnevaluatedMarkers)
            if (rawStringized.Contains(marker, StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>
    /// A printed NaN in an answer is never legitimate output here. In work/propcheck this
    /// single screen caught two wrong integrals that otherwise looked plausible
    /// (∫(sin²+cos²) answered `NaN * (integrand)`).
    ///
    /// Caveat worth knowing: `limit x->0 (1/x)` legitimately evaluates to NaN meaning "the
    /// limit does not exist". The limit tool therefore reports that case distinctly rather
    /// than calling it suspect.
    /// </summary>
    public static bool LooksLikeNaN(string rendered) =>
        rendered.Contains("NaN", StringComparison.Ordinal);
}
