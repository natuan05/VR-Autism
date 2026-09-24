using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using VRAutism.Gameplay.LessonGraphV2.Data;
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
            _root.Add(new PropertyField(serializedObject.FindProperty("_schemaVersion"), "Schema Version"));

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
                _root.Add(new PropertyField(nodesProperty, "Nodes") { name = "nodes-property" });
            }

            if (edgesProperty != null)
            {
                _root.Add(new PropertyField(edgesProperty, "Edges") { name = "edges-property" });
            }

            _root.TrackSerializedObjectValue(serializedObject, _ =>
            {
                RefreshEntryNodeField();
                RefreshValidationPanel();
            });
            return _root;
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
            var result = LessonGraphValidator.Validate((LessonGraph)target);
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
