# v0.1.0-beta.1 validation

- PASS registry rename, deferred disconnect, targeted block/unblock, PID reuse
- PASS duplicate Start is idempotent
- PASS 16 simultaneous clients, 48 correlated responses
- PASS registry reports real named-pipe client PIDs and connection count
- PASS disconnected client replaced while other clients remain usable
- PASS auth rejection isolated to offending client
- PASS real pipe selective disconnect, blocked reconnect, and unblock
- PASS Stop closes idle clients and completes pending requests
- PASS 20 start/connect/stop cycles
- PASS real package dry-run, install, idempotent install, fresh uninstall
- PASS existing registration backup and restore
- PASS restore protects later user changes
- PASS corrupted payload refused without changing registration
- PASS two independent release server processes initialize and expose 229 tools
- PASS two independent MCP clients read the active Revit view successfully; no model data retained

WPF rendering and rename/block/unblock button handlers also passed in an independent fixture. Actual Revit ribbon/window interaction and model-write workflows remain unverified. No model data is included.
