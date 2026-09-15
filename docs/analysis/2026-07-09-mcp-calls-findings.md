# MCP Calls Local Log Findings & Backlog

- **Date:** 2026-07-09
- **Scope:** Log analysis of local `rvt-mcp` calls from the 2026-07-09 session.

## 1. How to Enable Adaptive Bake for Evaluation

By default, adaptive bake is disabled and does not record local data for clustering. To enable it for evaluation:

1. **Option A: CLI Arguments**
   Start the MCP server with the following flags:
   ```bash
   rvt-mcp --enable-adaptive-bake --cache-send-code-bodies
   ```

2. **Option B: Environment Variables**
   Set the following variables in the environment launching the server:
   ```powershell
   $env:BIMWRIGHT_ENABLE_ADAPTIVE_BAKE = "1"
   $env:BIMWRIGHT_CACHE_SEND_CODE_BODIES = "1"
   ```

3. **Option C: JSON Configuration**
   Update `%LOCALAPPDATA%\RvtMcp\rvtmcp.config.json` with the keys:
   ```json
   {
     "enableAdaptiveBake": true,
     "cacheSendCodeBodies": true
   }
   ```

*Note: `cacheSendCodeBodies` is required if you want `send_code_to_revit` bodies to be eligible for clustering and suggestion generation.*

---

## 2. Session Analysis & Fail Rates

Based on log analysis of **1129 total calls** from the 2026-07-09 session:

- **Escape-hatch Dominance:** **546 calls (48.3%)** were `send_code_to_revit`. Since developers/agents resort to dynamic C# execution for nearly half of all operations, there is significant demand for custom tools.
- **Failures in `capture_view_image`:** There were **35 failures** for this tool. The primary root causes were:
  - Unexpanded environment variable literals (like `%TEMP%` and `%LOCALAPPDATA%`) sent directly by agents.
  - Path traversal attempts (`..`) or non-canonical paths.
  - UNC path usage or illegal character usage in custom folders.
  - Hard-gated allowlist constraints failing to inform the agent of resolved target folders.
- **Other Failures:** Compiler and syntax errors in `send_code_to_revit` (e.g., CS1061 missing definitions, CS1001, CS1503 type mismatches) account for a high proportion of execution failures.

---

## 3. How to Join Call Logs with SendCode Journals

Now that Workstream B (opt-in TTL send_code body persistence) has shipped, you can join the hashed call log with the detailed TTL journal for analysis:

- **Log Path (hashed):** `%LOCALAPPDATA%\RvtMcp\mcp-calls.jsonl`
- **Journal Path (redacted code):** `%LOCALAPPDATA%\RvtMcp\send-code-journal.jsonl`

### Join Key: `code_hash`
Both logs store a SHA1 hexadecimal digest of the raw code body. In `mcp-calls.jsonl`, it is nested under `params.code_hash`. In `send-code-journal.jsonl`, it is at the root `code_hash`.

### PowerShell Join Example:
```powershell
# Read the hashed call log
$Calls = Get-Content "$env:LOCALAPPDATA\RvtMcp\mcp-calls.jsonl" | ForEach-Object { ConvertFrom-Json $_ }
# Read the journal log
$Journal = Get-Content "$env:LOCALAPPDATA\RvtMcp\send-code-journal.jsonl" | ForEach-Object { ConvertFrom-Json $_ }

# Create a lookup map of hash -> redacted code
$JournalMap = @{}
foreach ($j in $Journal) {
    $JournalMap[$j.code_hash] = $j.code
}

# Output calls with their associated code bodies
foreach ($c in $Calls | Where-Object { $_.tool -eq "send_code_to_revit" }) {
    $Hash = $c.params.code_hash
    if ($JournalMap.ContainsKey($Hash)) {
        [PSCustomObject]@{
            Timestamp  = $c.timestamp
            Success    = $c.success
            DurationMs = $c.duration_ms
            CodeHash   = $Hash
            CodeBody   = $JournalMap[$Hash]
        }
    }
}
```

---

## 4. Backlog: Typed-Tool Prioritization (C.3 & C.4)

The prioritization of new typed tools based on call-mix analysis is deferred to a **follow-up product backlog**.

- **C.3 Typed Tools:** Designing and implementing native Revit tools to replace the most common `send_code` patterns (e.g., bulk element parameter edits, custom filtering, and view template creation) will be addressed in a future milestone.
- **C.4 Compiler Analysis:** Building tools to automatically repair common C# compiler errors (like CS1061 / CS1503) based on journaled bodies is out of scope for this plan.
