using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Validation;

namespace VRAutism.Gameplay.LessonGraphV2.Editor
{
    /// <summary>
    /// Focused authoring inspector for LessonGraph assets. It intentionally uses ordinary
    /// serialized property fields rather than introducing a graph canvas or changing the schema.
    /// </summary>
    [CustomEditor(typeof(LessonGraph))]
    public sealed class LessonGraphEditor : UnityEditor.Editor
    {
        private VisualElement _root;
        private VisualElement _entryFieldHost;
        private VisualElement _validationPanel;

        private void OnEnable()
        {
            Undo.undoRedoPerformed += HandleUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= HandleUndoRedo;
            _root = null;
            _entryFieldHost = null;
            _validationPanel = null;
        }

        public override VisualElement CreateInspectorGUI()
        {
            _root = new VisualElement { name = "lesson-graph-inspector" };
            var graph = target as LessonGraph;
            if (graph != null && graph.SchemaVersion == 1 && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(graph)))
            {
                var migrationButton = new Button(() => MigrateToSchemaTwo(graph))
                {
                    text = "Create Schema 2 Copy",
                    name = "schema-two-migration-button",
                    tooltip = "Validates and saves a separate schema-2 copy. The original remains unchanged."
                };
                _root.Add(migrationButton);
            }
            _root.Add(new Label($"Schema Version: {graph?.SchemaVersion ?? 0}")
            {
                name = "schema-version-label",
            });

            _entryFieldHost = new VisualElement { name = "entry-node-field-host" };
            _root.Add(_entryFieldHost);
            RefreshEntryNodeField();

            _validationPanel = new VisualElement { name = "validation-panel" };
            _root.Add(_validationPanel);
            RefreshValidationPanel();

            var nodesProperty = serializedObject.FindProperty("_nodes");
            var edgesProperty = serializedObject.FindProperty("_edges");
            if (nodesProperty != null)
            {
                _root.Add(CreateCollectionField(nodesProperty, "Nodes", "nodes-property", AddNode));
            }

            if (edgesProperty != null)
            {
                _root.Add(CreateCollectionField(edgesProperty, "Edges", "edges-property", AddEdge));
            }

            _root.TrackSerializedObjectValue(serializedObject, _ =>
            {
                RefreshEntryNodeField();
                RefreshValidationPanel();
            });
            return _root;
        }

        private ListView CreateCollectionField(
            SerializedProperty property, string label, string name, Action addItem)
        {
            // Keep Unity's serialized list binding, drawers, removal and reordering.
            // Its default add operation copies the last item's SerializeReference payload.
            var list = new ListView
            {
                name = name,
                bindingPath = property.propertyPath,
                showFoldoutHeader = true,
                headerTitle = label,
                showAddRemoveFooter = true,
                showBoundCollectionSize = false,
                reorderable = true,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                overridingAddButtonBehavior = (_, __) => addItem(),
            };
            list.SetEnabled(!serializedObject.isEditingMultipleObjects);
            return list;
        }

        private void AddNode()
        {
            serializedObject.Update();
            Undo.RecordObject(target, "Add Lesson Graph Node");
            var nodes = serializedObject.FindProperty("_nodes");
            var index = nodes.arraySize++;
            var node = nodes.GetArrayElementAtIndex(index);
            node.FindPropertyRelative("_id").stringValue = Guid.NewGuid().ToString("N");
            node.FindPropertyRelative("_nodeType").intValue = (int)NodeType.Quest;
            node.FindPropertyRelative("_position").vector2Value = Vector2.zero;
            node.FindPropertyRelative("_config").managedReferenceValue = new QuestNodeConfig();
            node.isExpanded = true;
            serializedObject.ApplyModifiedProperties();
        }

        private void AddEdge()
        {
            serializedObject.Update();
            Undo.RecordObject(target, "Add Lesson Graph Edge");
            var edges = serializedObject.FindProperty("_edges");
            var index = edges.arraySize++;
            var edge = edges.GetArrayElementAtIndex(index);
            edge.FindPropertyRelative("_fromNodeId").stringValue = string.Empty;
            edge.FindPropertyRelative("_toNodeId").stringValue = string.Empty;
            edge.FindPropertyRelative("_priority").intValue = 0;
            edge.FindPropertyRelative("_condition").managedReferenceValue = new StatusCondition(StatusCondition.Success);
            edge.isExpanded = true;
            serializedObject.ApplyModifiedProperties();
        }

        private void RefreshEntryNodeField()
        {
            if (_entryFieldHost == null || serializedObject == null)
            {
                return;
            }

            serializedObject.Update();
            var entryProperty = serializedObject.FindProperty("_entryNodeId");
            if (entryProperty == null)
            {
                return;
            }

            var validNodeIds = ReadNodeIds();
            var choices = new List<string> { string.Empty };
            choices.AddRange(validNodeIds);
            var currentValue = entryProperty.stringValue ?? string.Empty;
            if (!choices.Contains(currentValue))
            {
                choices.Add(currentValue);
            }

            var validIdSet = new HashSet<string>(validNodeIds);
            string FormatNodeId(string value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return "(none)";
                }

                return validIdSet.Contains(value) ? value : $"(missing) {value}";
            }

            var field = new PopupField<string>("Entry Node", choices, currentValue)
            {
                name = "entry-node-field",
            };
            field.formatListItemCallback = FormatNodeId;
            field.formatSelectedValueCallback = FormatNodeId;
            field.RegisterValueChangedCallback(evt => SetEntryNodeId(evt.newValue));

            _entryFieldHost.Clear();
            _entryFieldHost.Add(field);
        }

        private List<string> ReadNodeIds()
        {
            var ids = new List<string>();
            var seen = new HashSet<string>();
            var nodesProperty = serializedObject.FindProperty("_nodes");
            if (nodesProperty == null || !nodesProperty.isArray)
            {
                return ids;
            }

            for (var index = 0; index < nodesProperty.arraySize; index++)
            {
                var idProperty = nodesProperty.GetArrayElementAtIndex(index)?.FindPropertyRelative("_id");
                var id = idProperty?.stringValue;
                if (!string.IsNullOrEmpty(id) && seen.Add(id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        private void SetEntryNodeId(string value)
        {
            if (serializedObject == null || serializedObject.targetObject == null)
            {
                return;
            }

            serializedObject.Update();
            var entryProperty = serializedObject.FindProperty("_entryNodeId");
            if (entryProperty == null || entryProperty.stringValue == value)
            {
                return;
            }

            Undo.RecordObject(serializedObject.targetObject, "Change Lesson Graph Entry Node");
            entryProperty.stringValue = value ?? string.Empty;
            serializedObject.ApplyModifiedProperties();
            RefreshValidationPanel();
        }

        private void RefreshValidationPanel()
        {
            if (_validationPanel == null || target == null)
            {
                return;
            }

            _validationPanel.Clear();
            var graph = (LessonGraph)target;
            if (graph.SchemaVersion == 1)
                _validationPanel.Add(new HelpBox(
                    "Schema 1 assets remain supported. Advanced node and condition types require an explicit schema-2 copy.",
                    HelpBoxMessageType.Info));
            var result = LessonGraphValidator.Validate(graph);
            var summary = new Label(result.IsValid
                ? "Graph validation passed."
                : $"Graph validation found {result.Errors.Count} error(s).")
            {
                name = "validation-summary",
            };
            _validationPanel.Add(summary);

            for (var index = 0; index < result.Errors.Count; index++)
            {
                var error = result.Errors[index];
                _validationPanel.Add(new HelpBox(
                    $"{error.ErrorCode}: {error.Message}",
                    HelpBoxMessageType.Error)
                {
                    name = $"validation-error-{index}",
                });
            }
        }

        private static void MigrateToSchemaTwo(LessonGraph source)
        {
            if (LessonGraphSchemaMigration.TryCreateSchemaTwoCopy(source, out var copy, out _, out var error))
            {
                Selection.activeObject = copy;
                EditorGUIUtility.PingObject(copy);
            }
            else
            {
                EditorUtility.DisplayDialog("Lesson Graph Migration", error, "OK");
            }
        }

        private void HandleUndoRedo()
        {
            if (_root == null || serializedObject == null)
            {
                return;
            }

            serializedObject.Update();
            RefreshEntryNodeField();
            RefreshValidationPanel();
        }
    }
}
