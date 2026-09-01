using System.Collections.Generic;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;

namespace VRAutism.Gameplay.LessonGraphV2.Data
{
    /// <summary>
    /// Root ScriptableObject for a Phase 1 LessonGraph V2 asset.
    /// Stores all node and edge data as embedded serialized references (one asset file per lesson).
    /// Does NOT reference any scene MonoBehaviour or runtime objects.
    /// Binding resolution is deferred to LessonGraphBindings at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "NewLessonGraph", menuName = "VR-Autism/Lesson Graph V2")]
    public sealed class LessonGraph : ScriptableObject
    {
        [Tooltip("Increments when the graph schema changes. Used for snapshot/migration validation.")]
        [SerializeField] private int _schemaVersion = 1;

        [Tooltip("ID of the node where lesson execution begins. Must match a node in the nodes list.")]
        [SerializeField] private string _entryNodeId = string.Empty;

        [Tooltip("All nodes in this graph. Each node has a unique stable ID (GUID).")]
        [SerializeField] private List<LessonNodeData> _nodes = new List<LessonNodeData>();

        [Tooltip("All directed edges between nodes. Phase 1 conditions: AlwaysCondition or StatusCondition only.")]
        [SerializeField] private List<LessonEdgeData> _edges = new List<LessonEdgeData>();

        public int SchemaVersion => _schemaVersion;
        public string EntryNodeId => _entryNodeId;
        public IReadOnlyList<LessonNodeData> Nodes => _nodes;
        public IReadOnlyList<LessonEdgeData> Edges => _edges;

#if UNITY_EDITOR
        // Editor-only mutation helpers used by tests and custom inspectors.
        public void Editor_SetSchemaVersion(int v) => _schemaVersion = v;
        public void Editor_SetEntryNodeId(string id) => _entryNodeId = id;
        public void Editor_SetNodes(List<LessonNodeData> nodes) => _nodes = nodes ?? new List<LessonNodeData>();
        public void Editor_SetEdges(List<LessonEdgeData> edges) => _edges = edges ?? new List<LessonEdgeData>();
#endif
    }
}
