# AngouriMathMCP has moved

The MCP server is part of [AngouriMath](https://github.com/asc-community/AngouriMath) now, as
`amcli mcp`, in [`Sources/MCP`](https://github.com/asc-community/AngouriMath/tree/master/Sources/MCP):

```sh
dotnet tool install --global AngouriMath.Terminal
claude mcp add angourimath -- amcli mcp
amcli mcp --selftest
```

It builds against the library in the same repository, so it releases with each version, and its
tests run on every change to the library.

This repository is archived. Its history stays here.
