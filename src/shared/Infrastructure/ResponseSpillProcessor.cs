#nullable enable
using System;
using System.Text;
using Newtonsoft.Json.Linq;
using RvtMcp.Plugin.Views.Toast;

namespace RvtMcp.Plugin
{
    public sealed class ResponseSpillOutcome
    {
        public bool Spilled { get; set; }
        public bool Automatic { get; set; }
        public object? Data { get; set; }
    }

    /// <summary>
    /// Applies spill policy to one completed command result before it is logged or
    /// returned over the plugin transport boundary.
    /// </summary>
    public sealed class ResponseSpillProcessor
    {
        private readonly ResponseSpillWriter _writer;

        public ResponseSpillProcessor(ResponseSpillWriter writer)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        public ResponseSpillOutcome Process(
            string? commandName,
            string? paramsJson,
            bool commandSucceeded,
            object? data,
            string compactResponse)
        {
            if (!commandSucceeded)
                return Inline(data);

            var originalByteCount = Encoding.UTF8.GetByteCount(compactResponse ?? string.Empty);
            var decision = ResponseSpillPolicy.Evaluate(commandName, paramsJson, originalByteCount);
            if (!decision.ShouldSpill)
                return Inline(data);

            try
            {
                var payload = ResponseSpillPolicy.SelectPayload(commandName, data);
                var spill = _writer.Write(commandName ?? "response", payload, decision.Format);
                var envelope = spill.Envelope;
                envelope["command"] = commandName ?? string.Empty;
                envelope["automatic"] = decision.Automatic;
                envelope["original_response_byte_count"] = originalByteCount;
                if (decision.Automatic)
                {
                    envelope["note"] = "Oversized arbitrary output auto-spilled to a local same-machine file. Query it locally; do not re-run the executed command for the full result.";
                }
                EnsureEnvelopeBudget(envelope);
                return new ResponseSpillOutcome
                {
                    Spilled = true,
                    Automatic = decision.Automatic,
                    Data = envelope
                };
            }
            catch (Exception ex)
            {
                return new ResponseSpillOutcome
                {
                    Spilled = false,
                    Automatic = decision.Automatic,
                    Data = new JObject
                    {
                        ["success"] = false,
                        ["output_mode"] = "file",
                        ["spill_succeeded"] = false,
                        ["operation_completed"] = true,
                        ["mutation_applied"] = DetermineMutationApplied(commandName, paramsJson, data, originalByteCount),
                        ["original_response_byte_count"] = originalByteCount,
                        ["error"] = McpResponsePrivacy.RedactErrorForResponse("Failed to write spill file: " + ex.Message),
                        ["note"] = "The command already completed but its oversized output could not be persisted. Do not blindly retry a mutation."
                    }
                };
            }
        }

        private static JToken DetermineMutationApplied(
            string? commandName,
            string? paramsJson,
            object? data,
            int originalByteCount)
        {
            if (ResponseSizePolicyCatalog.IsMutationOutcomeIndeterminate(commandName))
                return JValue.CreateNull();

            var isMutation = ResponseSizePolicyCatalog.ShouldPreserveSuccessfulMutation(
                commandName,
                paramsJson,
                ToolActivityClassifier.Classify(commandName) == ToolActivityKind.Write);
            if (!isMutation)
                return new JValue(false);

            var compact = MutationResponseCompactor.Compact(data, originalByteCount);
            return compact["mutation_applied"]?.DeepClone() ?? new JValue(true);
        }

        private static ResponseSpillOutcome Inline(object? data)
        {
            return new ResponseSpillOutcome { Spilled = false, Automatic = false, Data = data };
        }

        private static void EnsureEnvelopeBudget(JObject envelope)
        {
            if (Encoding.UTF8.GetByteCount(envelope.ToString(Newtonsoft.Json.Formatting.None)) <= ResponseSizeGuard.EnforcementBudgetBytes)
                return;

            envelope["preview"] = string.Empty;
            envelope["preview_truncated"] = true;
            envelope["schema"] = new JObject
            {
                ["_truncated"] = "Schema omitted from envelope to preserve the response budget; inspect the local artifact directly."
            };
            envelope["schema_truncated"] = true;
        }
    }
}
