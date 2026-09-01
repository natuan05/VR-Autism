using System;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;

namespace VRAutism.Gameplay.LessonGraphV2.Data
{
    /// <summary>
    /// Serializable data for a single node in a LessonGraph.
    /// ID is a stable GUID string. Config is polymorphic via SerializeReference.
    /// Position stores editor layout data for future visual editor support.
    /// </summary>
    [Serializable]
    public sealed class LessonNodeData
    {
        [Tooltip("Stable GUID string. Must be unique within the LessonGraph.")]
        [SerializeField] private string _id = string.Empty;

        [SerializeField] private NodeType _nodeType = NodeType.Quest;

        [Tooltip("Editor layout position for future GraphView support.")]
        [SerializeField] private Vector2 _position = Vector2.zero;

        [Tooltip("Polymorphic config. Concrete type must match nodeType.")]
        [SerializeReference] private INodeConfig _config;

        public string Id => _id;
        public NodeType NodeType => _nodeType;
        public Vector2 Position => _position;
        public INodeConfig Config => _config;

        public LessonNodeData(string id, NodeType nodeType, INodeConfig config, Vector2 position = default)
        {
            _id = id ?? string.Empty;
            _nodeType = nodeType;
            _config = config;
            _position = position;
        }

        public LessonNodeData() { }
    }
}
