using System;
using System.Collections.Generic;
using System.Linq;
using RvtMcp.Server.Bake;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Server.Handlers
{
    public static class ListBakeSuggestionsHandler
    {
        public static string Handle(
            BakeDb db,
            IEnumerable<ClusterCandidate> candidates = null,
            SuggestionProposer proposer = null,
            DateTimeOffset? now = null)
        {
            return Handle(db, candidates, string.Empty, 0, 100, proposer, now);
        }

        public static string Handle(
            BakeDb db,
            IEnumerable<ClusterCandidate> candidates,
            string state,
            int startIndex,
            int limit,
            SuggestionProposer proposer = null,
            DateTimeOffset? now = null)
        {
            if (db == null)
                throw new ArgumentNullException(nameof(db));
            if (startIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(startIndex), "startIndex must be at least 0.");
            if (limit < 1 || limit > 500)
                throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and the hard maximum of 500.");

            if (candidates != null)
            {
                proposer ??= new SuggestionProposer();
                foreach (var suggestion in proposer.Propose(candidates, db.ListSuggestions(), now))
                    db.UpsertSuggestion(suggestion);
            }

            var filtered = db.ListSuggestions()
                .Where(s => !string.Equals(s.State, BakeSuggestionStates.Archived, StringComparison.Ordinal))
                .Where(s => string.IsNullOrWhiteSpace(state)
                    || string.Equals(s.State, state, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var suggestions = filtered
                .Skip(startIndex)
                .Take(limit)
                .Select(ToResponse)
                .ToArray();
            var nextIndex = startIndex + suggestions.Length;
            var truncated = nextIndex < filtered.Length;

            return new JObject
            {
                ["count"] = filtered.Length,
                ["start_index"] = startIndex,
                ["returned_count"] = suggestions.Length,
                ["truncated"] = truncated,
                ["next_index"] = truncated ? (JToken)nextIndex : JValue.CreateNull(),
                ["suggestions"] = new JArray(suggestions)
            }.ToString(Formatting.None);
        }

        public static string Handle(
            BakeDb db,
            UsageEventLogger usageLogger,
            DateTimeOffset? now = null,
            SuggestionProposer proposer = null)
        {
            var candidates = usageLogger?.RefreshCandidates(now);
            return Handle(db, candidates, proposer, now);
        }

        public static string Handle(
            BakeDb db,
            UsageEventLogger usageLogger,
            string state,
            int startIndex,
            int limit,
            DateTimeOffset? now = null,
            SuggestionProposer proposer = null)
        {
            var candidates = usageLogger?.RefreshCandidates(now);
            return Handle(db, candidates, state, startIndex, limit, proposer, now);
        }

        private static JObject ToResponse(BakeSuggestionRecord suggestion)
        {
            var payload = ParsePayload(suggestion.PayloadJson);
            return new JObject
            {
                ["id"] = suggestion.Id,
                ["title"] = suggestion.Title,
                ["source"] = suggestion.Source,
                ["score"] = suggestion.Score,
                ["state"] = suggestion.State,
                ["output_choices"] = payload["output_choices"] ?? new JArray("mcp_only", "ribbon_plus_mcp"),
                ["created_at"] = suggestion.CreatedAt
            };
        }

        private static JObject ParsePayload(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new JObject();
            try
            {
                return JObject.Parse(json);
            }
            catch (JsonException)
            {
                return new JObject();
            }
        }
    }
}
