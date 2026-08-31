using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json;

namespace RevitMCPCommandSet.Services.Memory
{
    /// <summary>
    /// PROJECT MEMORY - the model-scoped half of the memory layer.
    ///
    /// Stores an entity/relation graph INSIDE the Revit document, using Extensible
    /// Storage on ProjectInformation.
    ///
    /// Why in the document rather than a sidecar file:
    ///
    ///   1. A sidecar desynchronises. Copy the model, rename it, send it to a
    ///      colleague, roll back to yesterday's central file - the sidecar does not
    ///      follow, and what it says about the model stops being true silently.
    ///      Extensible Storage travels with the model, including through Save As,
    ///      worksharing and transmittal.
    ///
    ///   2. The previous store was a SQLite file resolved relative to the package
    ///      directory. Under the documented launch command (npx -y) that directory
    ///      lives in the npm cache, which npm may clear or re-resolve at any time -
    ///      so "persisted" user data sat in disposable storage.
    ///
    /// The graph is deliberately simple - entities with a kind, a name, an optional
    /// Revit element id, and free-form properties; relations as typed edges. That is
    /// enough to represent what a tool claims to have stored, which is the point: a
    /// store that cannot represent the thing it claims to have saved will report
    /// success and hold nothing.
    ///
    /// Extensible Storage API note: SchemaBuilder, Entity.Get/Set and AddArrayField
    /// are IDENTICAL across Revit 2022-2027, so nothing here needs a version guard.
    /// </summary>
    public static class ProjectMemoryStore
    {
        // A fixed GUID. Changing it orphans every graph already written into a model,
        // so it is a constant and must stay one.
        private static readonly Guid SchemaGuid = new Guid("7B1D9A64-2C58-4E7A-9F03-5D6E8C1A4B22");

        private const string SchemaName = "RevitMcpProjectMemory";
        private const string FieldChunks = "Chunks";
        private const string FieldFormat = "Format";
        private const string FieldUpdated = "UpdatedUtc";

        // Extensible Storage stores each array element as its own string. Chunking
        // keeps individual values comfortably small rather than betting on an
        // undocumented per-string ceiling.
        private const int ChunkSize = 4000;

        private static Schema GetOrCreateSchema()
        {
            Schema existing = Schema.Lookup(SchemaGuid);
            if (existing != null) return existing;

            SchemaBuilder builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetDocumentation(
                "Model-scoped memory written by mcp-servers-for-revit: an entity/relation graph " +
                "describing what AI tooling has recorded about this project.");

            // Public: the graph is not a secret, and a vendor-locked schema would make
            // the data unreadable by any other add-in, including a future version of
            // this one under a different assembly identity.
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);

            builder.AddArrayField(FieldChunks, typeof(string));
            builder.AddSimpleField(FieldFormat, typeof(string));
            builder.AddSimpleField(FieldUpdated, typeof(string));

            return builder.Finish();
        }

        private static Element Host(Document doc)
        {
            Element host = doc.ProjectInformation;
            if (host == null)
            {
                throw new InvalidOperationException(
                    "This document has no ProjectInformation element, so project memory has nowhere " +
                    "to live. Family documents are not supported.");
            }

            return host;
        }

        /// <summary>Read the stored graph. Returns an empty graph when nothing is stored.</summary>
        public static ProjectMemoryGraph Read(Document doc)
        {
            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return new ProjectMemoryGraph();

            Entity entity = Host(doc).GetEntity(schema);
            if (entity == null || !entity.IsValid()) return new ProjectMemoryGraph();

            IList<string> chunks = entity.Get<IList<string>>(FieldChunks);
            if (chunks == null || chunks.Count == 0) return new ProjectMemoryGraph();

            string json = string.Concat(chunks);
            if (string.IsNullOrWhiteSpace(json)) return new ProjectMemoryGraph();

            // A graph that will not parse is REPORTED, not silently replaced with an
            // empty one - overwriting it would destroy whatever is actually in there.
            ProjectMemoryGraph graph;
            try
            {
                graph = JsonConvert.DeserializeObject<ProjectMemoryGraph>(json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Project memory exists in this model but could not be parsed (" + ex.Message +
                    "). It has NOT been overwritten. Export it with project_memory_op action=raw " +
                    "before writing anything else.", ex);
            }

            return graph ?? new ProjectMemoryGraph();
        }

        /// <summary>Raw stored text, for recovery when Read cannot parse it.</summary>
        public static string ReadRaw(Document doc)
        {
            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return string.Empty;
            Entity entity = Host(doc).GetEntity(schema);
            if (entity == null || !entity.IsValid()) return string.Empty;
            IList<string> chunks = entity.Get<IList<string>>(FieldChunks);
            return chunks == null ? string.Empty : string.Concat(chunks);
        }

        /// <summary>
        /// Replace the stored graph. MUST be called inside an open transaction - this
        /// method does not open one, because the caller owns the transaction boundary.
        /// </summary>
        public static void Write(Document doc, ProjectMemoryGraph graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            string json = JsonConvert.SerializeObject(graph);
            var chunks = new List<string>();
            for (int i = 0; i < json.Length; i += ChunkSize)
            {
                chunks.Add(json.Substring(i, Math.Min(ChunkSize, json.Length - i)));
            }

            // An array field cannot be set to an empty list on every Revit version, so
            // an empty graph is stored as a single empty-object chunk instead.
            if (chunks.Count == 0) chunks.Add("{}");

            Schema schema = GetOrCreateSchema();
            Entity entity = new Entity(schema);
            entity.Set(FieldChunks, (IList<string>)chunks);
            entity.Set(FieldFormat, "revit-mcp-project-memory/1");
            entity.Set(FieldUpdated, DateTime.UtcNow.ToString("o"));

            Host(doc).SetEntity(entity);

            // Prove the write rather than assume it: read the chunks straight back and
            // compare. A store that reports success while holding nothing is the exact
            // defect this layer exists to fix.
            string readBack = ReadRaw(doc);
            if (readBack != json)
            {
                throw new InvalidOperationException(
                    "Project memory write-back check FAILED: " + json.Length + " characters were " +
                    "written but " + readBack.Length + " read back. Nothing should be assumed saved.");
            }
        }

        /// <summary>Remove the stored graph entirely.</summary>
        public static bool Clear(Document doc)
        {
            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return false;
            // DeleteEntity returns bool (true when an entity was actually removed),
            // not a count.
            return Host(doc).DeleteEntity(schema);
        }
    }
}
