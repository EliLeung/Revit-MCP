# H2-Revit contributor instructions

Modified from the upstream agent guide for H2-Revit on 2026-09-15.

- Current release scope: Windows x64, Revit 2026; preserve Apache-2.0 license and original attribution.
- Keep Revit model edits on the existing ExternalEvent queue and retain transaction/undo behavior.
- Build: dotnet build src/plugin-r26/RvtMcp.Plugin.R26.csproj -c Release -p:RvtMcpSkipDeploy=true
- Transport checks: dotnet run --project tests/H2.TransportTests/TransportTests.csproj -c Release
- Do not commit credentials, model files, discovery tokens, personal MCP configs, logs, or binary build output.
- Installer changes need sandbox install/uninstall testing. The installer accepts -TestRoot for isolated tests.
- Do not claim end-to-end Revit verification based solely on standalone transport tests.
