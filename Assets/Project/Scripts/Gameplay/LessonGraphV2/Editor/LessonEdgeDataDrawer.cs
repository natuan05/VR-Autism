using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;

namespace VRAutism.Gameplay.LessonGraphV2.Editor
{
    /// <summary>
    /// UI Toolkit authoring for LessonGraph edges. Unknown managed references remain untouched
    /// until the author explicitly chooses a replacement.
    /// </summary>
    [CustomPropertyDrawer(typeof(LessonEdgeData))]
    public sealed class LessonEdgeDataDrawer : PropertyDrawer
    {
        private static readonly Regex s_edgeConditionPathPattern = new Regex(
            @"(?:^|\.)_edges\.Array\.data\[(\d+)\]\._condition$",
            RegexOptions.Compiled);

        private static readonly Regex s_inlineConditionRidPattern = new Regex(
            @"^_condition:\s*(?:\{\s*)?rid:\s*(-?\d+)",
            RegexOptions.Compiled);

        private static readonly Regex s_ridPattern = new Regex(
            @"^rid:\s*(-?\d+)",
            RegexOptions.Compiled);

        private static readonly string[] s_conditionTypes =
        {
            nameof(StatusCondition),
            nameof(AlwaysCondition),
        };

        private static readonly string[] s_statuses =
        {
            StatusCondition.Success,
            StatusCondition.Skipped,
            StatusCondition.Timeout,
            StatusCondition.Failed,
        };

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement { name = "lesson-edge-data-drawer" };
            if (property == null || property.serializedObject == null)
            {
                return root;
            }

            if (property.serializedObject.isEditingMultipleObjects)
            {
                var warning = new HelpBox(
                    "Editing multiple LessonGraph edges at once is not supported. Select one graph asset to edit edge data safely.",
                    HelpBoxMessageType.Warning)
                {
                    name = "multi-edit-warning",
                };
                root.Add(warning);
                return root;
            }

            InitializeNullCondition(property);
            Rebuild(root, property);
            root.TrackSerializedObjectValue(property.serializedObject, _ => Rebuild(root, property));
            return root;
        }

        private static void InitializeNullCondition(SerializedProperty edgeProperty)
        {
            var serializedObject = edgeProperty.serializedObject;
            if (serializedObject == null || serializedObject.targetObject == null)
            {
                return;
            }

            serializedObject.Update();
            var currentEdge = serializedObject.FindProperty(edgeProperty.propertyPath);
            var conditionProperty = currentEdge?.FindPropertyRelative("_condition");
            if (conditionProperty == null || conditionProperty.managedReferenceValue != null ||
                IsMissingCondition(conditionProperty))
            {
                return;
            }

            Undo.RecordObject(serializedObject.targetObject, "Initialize Lesson Edge Condition");
            conditionProperty.managedReferenceValue = new StatusCondition(StatusCondition.Success);
            serializedObject.ApplyModifiedProperties();
        }

        private static void Rebuild(VisualElement root, SerializedProperty edgeProperty)
        {
            root.Clear();
            var serializedObject = edgeProperty?.serializedObject;
            if (serializedObject == null)
            {
                return;
            }

            var currentEdge = serializedObject.FindProperty(edgeProperty.propertyPath);
            var fromProperty = currentEdge?.FindPropertyRelative("_fromNodeId");
            var toProperty = currentEdge?.FindPropertyRelative("_toNodeId");
            var priorityProperty = currentEdge?.FindPropertyRelative("_priority");
            var conditionProperty = currentEdge?.FindPropertyRelative("_condition");
            if (fromProperty == null || toProperty == null || priorityProperty == null || conditionProperty == null)
            {
                return;
            }

            var container = new VisualElement { name = "edge-fields" };
            var nodeIds = ReadNodeIds(serializedObject);
            container.Add(CreateNodeIdField("From Node", "from-node-field", fromProperty, nodeIds));
            container.Add(CreateNodeIdField("To Node", "to-node-field", toProperty, nodeIds));

            var priorityField = new IntegerField("Priority")
            {
                name = "priority-field",
                value = priorityProperty.intValue,
            };
            priorityField.RegisterValueChangedCallback(evt =>
                SetIntValue(priorityProperty, evt.newValue, "Change Lesson Edge Priority"));
            container.Add(priorityField);

            AddConditionControls(container, root, currentEdge, conditionProperty);
            root.Add(container);
        }

        private static PopupField<string> CreateNodeIdField(
            string label,
            string name,
            SerializedProperty nodeIdProperty,
            IReadOnlyList<string> existingNodeIds)
        {
            var validIds = new HashSet<string>(existingNodeIds);
            var choices = new List<string>(existingNodeIds);
            if (!choices.Contains(string.Empty))
            {
                choices.Insert(0, string.Empty);
            }

            var currentValue = nodeIdProperty.stringValue ?? string.Empty;
            if (!choices.Contains(currentValue))
            {
                choices.Add(currentValue);
            }

            var field = new PopupField<string>(label, choices, currentValue)
            {
                name = name,
            };
            string FormatNodeId(string value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return "(none)";
                }

                return validIds.Contains(value) ? value : $"(missing) {value}";
            }

            field.formatListItemCallback = FormatNodeId;
            field.formatSelectedValueCallback = FormatNodeId;
            field.RegisterValueChangedCallback(evt =>
                SetStringValue(nodeIdProperty, evt.newValue, $"Change Lesson Edge {label}"));
            return field;
        }

        private static void AddConditionControls(
            VisualElement container,
            VisualElement root,
            SerializedProperty edgeProperty,
            SerializedProperty conditionProperty)
        {
            if (IsMissingCondition(conditionProperty))
            {
                AddReplacementWarning(
                    container,
                    root,
                    "missing-condition-warning",
                    "This edge condition has a missing managed-reference type (RID -2). Its serialized payload is preserved.",
                    edgeProperty,
                    conditionProperty);
                return;
            }

            var condition = conditionProperty.managedReferenceValue as IEdgeCondition;
            if (condition == null)
            {
                AddReplacementWarning(
                    container,
                    root,
                    "null-condition-warning",
                    "This edge has no condition. Replace it explicitly with StatusCondition (success) to continue editing.",
                    edgeProperty,
                    conditionProperty);
                return;
            }

            if (!(condition is StatusCondition) && !(condition is AlwaysCondition))
            {
                AddReplacementWarning(
                    container,
                    root,
                    "unsupported-condition-warning",
                    $"Condition type '{condition.GetType().Name}' is not supported by this editor. Its payload is preserved.",
                    edgeProperty,
                    conditionProperty);
                return;
            }

            var currentType = condition is StatusCondition ? nameof(StatusCondition) : nameof(AlwaysCondition);
            var typeField = new PopupField<string>(
                "Condition Type",
                new List<string>(s_conditionTypes),
                currentType)
            {
                name = "condition-type-field",
            };
            typeField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue == currentType)
                {
                    return;
                }

                IEdgeCondition replacement = evt.newValue == nameof(AlwaysCondition)
                    ? (IEdgeCondition)new AlwaysCondition()
                    : new StatusCondition(StatusCondition.Success);
                SetManagedReference(conditionProperty, replacement, "Change Lesson Edge Condition");
                Rebuild(root, edgeProperty);
            });
            container.Add(typeField);

            if (condition is StatusCondition)
            {
                var statusProperty = conditionProperty.FindPropertyRelative("_requiredStatus");
                if (statusProperty == null)
                {
                    return;
                }

                var statusField = new PopupField<string>(
                    "Required Status",
                    new List<string>(s_statuses),
                    statusProperty.stringValue)
                {
                    name = "required-status-field",
                };
                statusField.RegisterValueChangedCallback(evt =>
                    SetStringValue(statusProperty, evt.newValue, "Change Lesson Edge Status"));
                container.Add(statusField);
            }
        }

        private static void AddReplacementWarning(
            VisualElement container,
            VisualElement root,
            string warningName,
            string message,
            SerializedProperty edgeProperty,
            SerializedProperty conditionProperty)
        {
            var warning = new HelpBox(message, HelpBoxMessageType.Error) { name = warningName };
            container.Add(warning);

            var replaceButton = new Button(() =>
            {
                SetManagedReference(
                    conditionProperty,
                    new StatusCondition(StatusCondition.Success),
                    "Replace Lesson Edge Condition");
                Rebuild(root, edgeProperty);
            })
            {
                name = "replace-condition-button",
                text = "Replace with StatusCondition (success)",
                tooltip = "Explicitly replace the stored condition payload with a new StatusCondition.",
            };
            container.Add(replaceButton);
        }

        private static bool IsMissingCondition(SerializedProperty conditionProperty)
        {
            // A live value means this property has already been explicitly repaired/replaced.
            // Unity may still report the old missing RID until the asset is saved and reloaded.
            if (conditionProperty == null || conditionProperty.managedReferenceValue != null)
            {
                return false;
            }

            var serializedObject = conditionProperty?.serializedObject;
            var target = serializedObject?.targetObject;
            if (target == null ||
                !SerializationUtility.HasManagedReferencesWithMissingTypes(target))
            {
                return false;
            }

            var missingTypes = SerializationUtility.GetManagedReferencesWithMissingTypes(target);
            if (TryGetPersistedConditionReferenceId(conditionProperty, out var referenceId, out var hasPersistedReference))
            {
                if (!hasPersistedReference)
                {
                    return false;
                }

                for (var i = 0; i < missingTypes.Length; i++)
                {
                    if (missingTypes[i].referenceId == referenceId)
                    {
                        return true;
                    }
                }

                return false;
            }

            // If Unity reports missing types but the asset cannot be resolved to a persisted RID,
            // preserve the null-looking value rather than risk overwriting unrecoverable payload.
            return missingTypes.Length > 0 && conditionProperty.managedReferenceId == -2;
        }

        private static bool TryGetPersistedConditionReferenceId(
            SerializedProperty conditionProperty,
            out long referenceId,
            out bool hasPersistedReference)
        {
            referenceId = default;
            hasPersistedReference = false;
            var pathMatch = s_edgeConditionPathPattern.Match(conditionProperty.propertyPath);
            if (!pathMatch.Success || !int.TryParse(pathMatch.Groups[1].Value, out var edgeIndex))
            {
                return false;
            }

            var assetPath = AssetDatabase.GetAssetPath(conditionProperty.serializedObject.targetObject);
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(projectRoot))
            {
                return false;
            }

            var absolutePath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            if (!File.Exists(absolutePath))
            {
                return false;
            }

            try
            {
                return TryReadConditionReferenceId(
                    File.ReadAllLines(absolutePath), edgeIndex, out referenceId, out hasPersistedReference);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool TryReadConditionReferenceId(
            IReadOnlyList<string> lines,
            int edgeIndex,
            out long referenceId,
            out bool hasPersistedReference)
        {
            referenceId = default;
            hasPersistedReference = false;
            var edgesIndent = -1;
            var currentEdgeIndex = -1;
            var requestedEdgeFound = false;
            var sawRecognizedEdge = false;
            var explicitEmptyList = false;

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var trimmed = line.Trim();
                if (edgesIndent < 0)
                {
                    if (trimmed == "_edges: []")
                    {
                        edgesIndent = CountLeadingWhitespace(line);
                        explicitEmptyList = true;
                    }
                    else if (trimmed == "_edges:")
                    {
                        edgesIndent = CountLeadingWhitespace(line);
                    }

                    continue;
                }

                if (trimmed.Length == 0)
                {
                    continue;
                }

                var indent = CountLeadingWhitespace(line);
                var isEdgeItem = trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed == "-";
                if (indent < edgesIndent)
                {
                    return (sawRecognizedEdge || explicitEmptyList) && !requestedEdgeFound;
                }

                if (indent == edgesIndent && !isEdgeItem)
                {
                    return trimmed == "references:" &&
                        (sawRecognizedEdge || explicitEmptyList) && !requestedEdgeFound;
                }

                if (isEdgeItem)
                {
                    if (!trimmed.StartsWith("- _fromNodeId:", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    if (requestedEdgeFound)
                    {
                        return false;
                    }

                    currentEdgeIndex++;
                    sawRecognizedEdge = true;
                    requestedEdgeFound = currentEdgeIndex == edgeIndex;
                }

                if (currentEdgeIndex != edgeIndex)
                {
                    continue;
                }

                var inlineMatch = s_inlineConditionRidPattern.Match(trimmed);
                if (inlineMatch.Success)
                {
                    if (long.TryParse(inlineMatch.Groups[1].Value, out referenceId))
                    {
                        hasPersistedReference = true;
                        return true;
                    }

                    return false;
                }

                if (trimmed != "_condition:")
                {
                    continue;
                }

                for (var ridLineIndex = i + 1; ridLineIndex < lines.Count; ridLineIndex++)
                {
                    var ridLine = lines[ridLineIndex];
                    var ridTrimmed = ridLine.Trim();
                    if (ridTrimmed.Length == 0)
                    {
                        continue;
                    }

                    if (CountLeadingWhitespace(ridLine) <= indent)
                    {
                        return false;
                    }

                    var ridMatch = s_ridPattern.Match(ridTrimmed);
                    if (ridMatch.Success && long.TryParse(ridMatch.Groups[1].Value, out referenceId))
                    {
                        hasPersistedReference = true;
                        return true;
                    }

                    return false;
                }

                return false;
            }

            return edgesIndent >= 0 && (sawRecognizedEdge || explicitEmptyList) && !requestedEdgeFound;
        }

        private static int CountLeadingWhitespace(string value)
        {
            var count = 0;
            while (count < value.Length && char.IsWhiteSpace(value[count]))
            {
                count++;
            }

            return count;
        }

        private static List<string> ReadNodeIds(SerializedObject serializedObject)
        {
            var result = new List<string>();
            var seen = new HashSet<string>();
            var nodesProperty = serializedObject.FindProperty("_nodes");
            if (nodesProperty == null || !nodesProperty.isArray)
            {
                return result;
            }

            for (var index = 0; index < nodesProperty.arraySize; index++)
            {
                var nodeProperty = nodesProperty.GetArrayElementAtIndex(index);
                var idProperty = nodeProperty?.FindPropertyRelative("_id");
                var id = idProperty?.stringValue;
                if (!string.IsNullOrEmpty(id) && seen.Add(id))
                {
                    result.Add(id);
                }
            }

            return result;
        }

        private static void SetStringValue(SerializedProperty property, string value, string undoName)
        {
            var serializedObject = property?.serializedObject;
            if (serializedObject == null || serializedObject.targetObject == null)
            {
                return;
            }

            serializedObject.Update();
            var currentProperty = serializedObject.FindProperty(property.propertyPath);
            if (currentProperty == null || currentProperty.stringValue == value)
            {
                return;
            }

            Undo.RecordObject(serializedObject.targetObject, undoName);
            currentProperty.stringValue = value ?? string.Empty;
            serializedObject.ApplyModifiedProperties();
        }

        private static void SetIntValue(SerializedProperty property, int value, string undoName)
        {
            var serializedObject = property?.serializedObject;
            if (serializedObject == null || serializedObject.targetObject == null)
            {
                return;
            }

            serializedObject.Update();
            var currentProperty = serializedObject.FindProperty(property.propertyPath);
            if (currentProperty == null || currentProperty.intValue == value)
            {
                return;
            }

            Undo.RecordObject(serializedObject.targetObject, undoName);
            currentProperty.intValue = value;
            serializedObject.ApplyModifiedProperties();
        }

        private static void SetManagedReference(
            SerializedProperty property,
            IEdgeCondition value,
            string undoName)
        {
            var serializedObject = property?.serializedObject;
            if (serializedObject == null || serializedObject.targetObject == null)
            {
                return;
            }

            serializedObject.Update();
            var currentProperty = serializedObject.FindProperty(property.propertyPath);
            if (currentProperty == null)
            {
                return;
            }

            Undo.RecordObject(serializedObject.targetObject, undoName);
            currentProperty.managedReferenceValue = value;
            serializedObject.ApplyModifiedProperties();
        }
    }
}
