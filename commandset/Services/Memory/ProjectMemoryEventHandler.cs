using RevitMCPCommandSet.Utils;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Memory
{
    /// <summary>
    /// Reads and writes the model-scoped memory graph. Writes go through an
    /// ExternalEvent and an owning transaction, as every document write must.
    /// </summary>
    public class ProjectMemoryEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication _uiApp;
        private Document _doc => _uiApp.ActiveUIDocument.Document;


        public string Action { get; private set; }
        public JObject Payload { get; private set; }

        public AIResult<object> Result { get; private set; }

        public void SetParameters(string action, JObject payload)
        {
            Action = action;
            Payload = payload;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication uiapp)
        {
            _uiApp = uiapp;

            try
            {
                switch ((Action ?? string.Empty).ToLowerInvariant())
                {
                    case "read":
                        DoRead();
                        break;
                    case "query":
                        DoQuery();
                        break;
                    case "write":
                        DoWrite();
                        break;
                    case "stats":
                        DoStats();
                        break;
                    case "raw":
                        DoRaw();
                        break;
                    case "clear":
                        DoClear();
                        break;
                    default:
                        Result = new AIResult<object>
                        {
                            Success = false,
                            Message =
                                $"Unknown action '{Action}'. Use read, query, write, stats, raw or clear.",
                            Response = null
                        };
                        break;
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Project memory error: {ex.Message}",
                    Response = null
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private void DoRead()
        {
            ProjectMemoryGraph graph = ProjectMemoryStore.Read(_doc);
            Result = new AIResult<object>
            {
                Success = true,
                Message = $"{graph.Entities.Count} entities, {graph.Relations.Count} relations.",
                Response = graph
            };
        }

        private void DoRaw()
        {
            string raw = ProjectMemoryStore.ReadRaw(_doc);
            Result = new AIResult<object>
            {
                Success = true,
                Message = raw.Length == 0
                    ? "No project memory is stored in this model."
                    : $"{raw.Length} characters stored.",
                Response = raw
            };
        }

        private void DoQuery()
        {
            ProjectMemoryGraph graph = ProjectMemoryStore.Read(_doc);
            string kind = Payload?["kind"]?.ToString();
            string name = Payload?["name"]?.ToString();
            string relation = Payload?["relation"]?.ToString();
            int limit = Payload?["limit"]?.ToObject<int>() ?? 100;

            IEnumerable<MemoryEntity> entities = graph.Entities;
            if (!string.IsNullOrWhiteSpace(kind))
                entities = entities.Where(e => string.Equals(e.Kind, kind, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(name))
                entities = entities.Where(e =>
                    (e.Name ?? string.Empty).IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (e.Id ?? string.Empty).IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);

            List<MemoryEntity> hits = entities.Take(limit).ToList();
            var ids = new HashSet<string>(hits.Select(e => e.Id));

            IEnumerable<MemoryRelation> rels = graph.Relations
                .Where(r => ids.Contains(r.From) || ids.Contains(r.To));
            if (!string.IsNullOrWhiteSpace(relation))
                rels = rels.Where(r => string.Equals(r.Kind, relation, StringComparison.OrdinalIgnoreCase));

            // Say what was searched, not just what matched: "0 results" from an EMPTY
            // store and "0 results" from a store with 4,000 entities mean different
            // things, and the caller cannot tell them apart otherwise.
            Result = new AIResult<object>
            {
                Success = true,
                Message = graph.Entities.Count == 0
                    ? "This model holds no project memory yet, so there was nothing to search."
                    : $"{hits.Count} of {graph.Entities.Count} entities matched.",
                Response = new { entities = hits, relations = rels.ToList(), searched = graph.Entities.Count }
            };
        }

        private void DoWrite()
        {
            List<MemoryEntity> entities =
                Payload?["entities"]?.ToObject<List<MemoryEntity>>() ?? new List<MemoryEntity>();
            List<MemoryRelation> relations =
                Payload?["relations"]?.ToObject<List<MemoryRelation>>() ?? new List<MemoryRelation>();

            if (entities.Count == 0 && relations.Count == 0)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = "Nothing to write: supply entities, relations, or both.",
                    Response = null
                };
                return;
            }

            ProjectMemoryGraph graph = ProjectMemoryStore.Read(_doc);
            var (added, updated) = graph.Upsert(entities);
            var (linked, skipped, dangling) = graph.Link(relations);

            using (Transaction tx = new Transaction(_doc, "Write project memory"))
            {
                tx.Start();
                ProjectMemoryStore.Write(_doc, graph);
                tx.Commit();
            }

            string message =
                $"Stored: {added} entity/entities added, {updated} updated, {linked} relation(s) linked.";
            if (skipped > 0) message += $" {skipped} relation(s) skipped.";
            if (dangling.Count > 0)
            {
                message += " Dangling (endpoint not in the graph, so NOT stored): " +
                           string.Join("; ", dangling.Take(5)) +
                           (dangling.Count > 5 ? $" and {dangling.Count - 5} more." : ".");
            }

            Result = new AIResult<object>
            {
                Success = added + updated + linked > 0,
                Message = message,
                Response = new
                {
                    added,
                    updated,
                    linked,
                    skipped,
                    dangling,
                    totalEntities = graph.Entities.Count,
                    totalRelations = graph.Relations.Count
                }
            };
        }

        private void DoStats()
        {
            ProjectMemoryGraph graph = ProjectMemoryStore.Read(_doc);
            var byKind = graph.Entities
                .GroupBy(e => string.IsNullOrWhiteSpace(e.Kind) ? "(none)" : e.Kind)
                .ToDictionary(g => g.Key, g => g.Count());
            var byRelation = graph.Relations
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Kind) ? "(none)" : r.Kind)
                .ToDictionary(g => g.Key, g => g.Count());

            Result = new AIResult<object>
            {
                Success = true,
                Message = $"{graph.Entities.Count} entities, {graph.Relations.Count} relations, " +
                          $"stored in the model via Extensible Storage.",
                Response = new
                {
                    entities = graph.Entities.Count,
                    relations = graph.Relations.Count,
                    byKind,
                    byRelation,
                    storage = "Revit Extensible Storage on ProjectInformation",
                    document = _doc.Title
                }
            };
        }

        private void DoClear()
        {
            bool removed;
            using (Transaction tx = new Transaction(_doc, "Clear project memory"))
            {
                tx.Start();
                removed = ProjectMemoryStore.Clear(_doc);
                tx.Commit();
            }

            Result = new AIResult<object>
            {
                Success = removed,
                Message = removed
                    ? "Project memory removed from this model."
                    : "There was no project memory in this model to remove.",
                Response = removed
            };
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 15000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public string GetName()
        {
            return "Project Memory";
        }
    }
}
