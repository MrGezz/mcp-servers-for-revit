using Newtonsoft.Json;

namespace RevitMCPCommandSet.Services.Memory
{
    /// <summary>
    /// The shape of model-scoped memory: entities, and typed edges between them.
    ///
    /// Deliberately general. The flat "projects and rooms" tables this replaces could
    /// only represent two things, so every other kind of fact a tool wanted to record
    /// had nowhere to go - and a store that cannot represent what it is asked to save
    /// ends up reporting success and holding nothing.
    /// </summary>
    public class ProjectMemoryGraph
    {
        [JsonProperty("entities")]
        public List<MemoryEntity> Entities { get; set; } = new List<MemoryEntity>();

        [JsonProperty("relations")]
        public List<MemoryRelation> Relations { get; set; } = new List<MemoryRelation>();

        /// <summary>
        /// Insert or update by id. Returns how many were added and how many updated,
        /// so a caller can be told what actually happened rather than just "ok".
        /// </summary>
        public (int added, int updated) Upsert(IEnumerable<MemoryEntity> incoming)
        {
            int added = 0, updated = 0;
            foreach (MemoryEntity e in incoming ?? Enumerable.Empty<MemoryEntity>())
            {
                if (e == null || string.IsNullOrWhiteSpace(e.Id)) continue;
                MemoryEntity existing = Entities.FirstOrDefault(x => x.Id == e.Id);
                if (existing == null)
                {
                    Entities.Add(e);
                    added++;
                }
                else
                {
                    existing.Kind = e.Kind ?? existing.Kind;
                    existing.Name = e.Name ?? existing.Name;
                    existing.ElementId = e.ElementId != 0 ? e.ElementId : existing.ElementId;
                    if (e.Props != null)
                    {
                        existing.Props ??= new Dictionary<string, string>();
                        foreach (var kv in e.Props) existing.Props[kv.Key] = kv.Value;
                    }

                    updated++;
                }
            }

            return (added, updated);
        }

        /// <summary>Add edges, skipping exact duplicates and edges with a missing endpoint.</summary>
        public (int added, int skipped, List<string> dangling) Link(IEnumerable<MemoryRelation> incoming)
        {
            int added = 0, skipped = 0;
            var dangling = new List<string>();
            var ids = new HashSet<string>(Entities.Select(e => e.Id));

            foreach (MemoryRelation r in incoming ?? Enumerable.Empty<MemoryRelation>())
            {
                if (r == null || string.IsNullOrWhiteSpace(r.From) ||
                    string.IsNullOrWhiteSpace(r.To) || string.IsNullOrWhiteSpace(r.Kind))
                {
                    skipped++;
                    continue;
                }

                // A relation pointing at an entity that is not there is REPORTED rather
                // than stored: a graph full of dangling edges answers queries wrongly
                // and nobody finds out.
                if (!ids.Contains(r.From) || !ids.Contains(r.To))
                {
                    dangling.Add($"{r.From} -[{r.Kind}]-> {r.To}");
                    skipped++;
                    continue;
                }

                if (Relations.Any(x => x.From == r.From && x.To == r.To && x.Kind == r.Kind))
                {
                    skipped++;
                    continue;
                }

                Relations.Add(r);
                added++;
            }

            return (added, skipped, dangling);
        }
    }

    public class MemoryEntity
    {
        /// <summary>Caller-chosen stable identifier, e.g. "room:L1-101" or "material:concrete-c30".</summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>What sort of thing this is, e.g. "room", "material", "decision", "standard".</summary>
        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>The Revit element this describes, when it describes one. 0 when it does not.</summary>
        [JsonProperty("elementId")]
        public long ElementId { get; set; }

        [JsonProperty("props")]
        public Dictionary<string, string> Props { get; set; }
    }

    public class MemoryRelation
    {
        [JsonProperty("from")]
        public string From { get; set; }

        [JsonProperty("to")]
        public string To { get; set; }

        /// <summary>Edge type, e.g. "contains", "serves", "supersedes".</summary>
        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("props")]
        public Dictionary<string, string> Props { get; set; }
    }
}
