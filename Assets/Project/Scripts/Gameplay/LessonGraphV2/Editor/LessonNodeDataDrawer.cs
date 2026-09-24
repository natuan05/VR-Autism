using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;

namespace VRAutism.Gameplay.LessonGraphV2.Editor
{
    /// <summary>
    /// Status of synchronization between LessonNodeData._nodeType and _config.
    /// </summary>
    public enum NodeSyncStatus
    {
        Compatible,
        NullConfig,
        Mismatch,
        UnsupportedPhase2
    }

    /// <summary>
    /// Editor synchronization and repair helper for LessonNodeData.
    /// Operates on SerializedProperty and SerializedObject for Undo safety and dirty tracking.
    /// </summary>
    public static class LessonNodeSyncHelper
    {
        private static readonly Regex s_nodeConfigPathPattern = new Regex(
            @"(?:^|\.)_nodes\.Array\.data\[(\d+)\]\._config$",
            RegexOptions.Compiled);

        private static readonly Regex s_inlineConfigRidPattern = new Regex(
            @"^_config:\s*(?:\{\s*)?rid:\s*(-?\d+)",
            RegexOptions.Compiled);

        private static readonly Regex s_ridPattern = new Regex(
            @"^rid:\s*(-?\d+)",
            RegexOptions.Compiled);

        private static readonly Dictionary<NodeType, Type> s_nodeTypeToConfigType = new Dictionary<NodeType, Type>
        {
            { NodeType.Quest, typeof(QuestNodeConfig) },
            { NodeType.Dialogue, typeof(DialogueNodeConfig) },
            { NodeType.Wait, typeof(WaitNodeConfig) },
            { NodeType.Checkpoint, typeof(CheckpointNodeConfig) }
        };

        private static readonly Dictionary<Type, NodeType> s_configTypeToNodeType = new Dictionary<Type, NodeType>
        {
            { typeof(QuestNodeConfig), NodeType.Quest },
            { typeof(DialogueNodeConfig), NodeType.Dialogue },
            { typeof(WaitNodeConfig), NodeType.Wait },
            { typeof(CheckpointNodeConfig), NodeType.Checkpoint }
        };

        /// <summary>
        /// Returns true if nodeType is a supported Phase 1 type (Quest, Dialogue, Wait, Checkpoint).
        /// </summary>
        public static bool IsSupportedPhase1Type(NodeType nodeType)
        {
            return Enum.IsDefined(typeof(NodeType), nodeType) && s_nodeTypeToConfigType.ContainsKey(nodeType);
        }

        /// <summary>
        /// Returns the expected concrete INodeConfig Type for the given NodeType, or null if unsupported.
        /// </summary>
        public static Type GetExpectedConfigType(NodeType nodeType)
        {
            return s_nodeTypeToConfigType.TryGetValue(nodeType, out var configType) ? configType : null;
        }

        /// <summary>
        /// Attempts to map a concrete config Type back to its corresponding Phase 1 NodeType.
        /// </summary>
        public static bool TryGetNodeTypeForConfigType(Type configType, out NodeType nodeType)
        {
            if (configType != null && s_configTypeToNodeType.TryGetValue(configType, out nodeType))
            {
                return true;
            }

            nodeType = default;
            return false;
        }

        /// <summary>
        /// Creates a default INodeConfig instance for the specified Phase 1 NodeType.
        /// Throws NotSupportedException if nodeType is a Phase 2 or unsupported type.
        /// </summary>
        public static INodeConfig CreateDefaultConfig(NodeType nodeType)
        {
            switch (nodeType)
            {
                case NodeType.Quest:
                    return new QuestNodeConfig();
                case NodeType.Dialogue:
                    return new DialogueNodeConfig();
                case NodeType.Wait:
                    return new WaitNodeConfig();
                case NodeType.Checkpoint:
                    return new CheckpointNodeConfig();
                default:
                    throw new NotSupportedException($"Node type '{nodeType}' is not supported in Phase 1.");
            }
        }

        /// <summary>
        /// Evaluates synchronization status between discriminator nodeType and payload config.
        /// </summary>
        public static NodeSyncStatus GetSyncStatus(NodeType nodeType, INodeConfig config)
        {
            if (!IsSupportedPhase1Type(nodeType))
            {
                return NodeSyncStatus.UnsupportedPhase2;
            }

            if (config == null)
            {
                return NodeSyncStatus.NullConfig;
            }

            var expectedType = GetExpectedConfigType(nodeType);
            if (expectedType != null && config.GetType() == expectedType)
            {
                return NodeSyncStatus.Compatible;
            }

            return NodeSyncStatus.Mismatch;
        }

        /// <summary>
        /// Evaluates synchronization status of a LessonNodeData instance.
        /// </summary>
        public static NodeSyncStatus GetSyncStatus(LessonNodeData nodeData)
        {
            if (nodeData == null) throw new ArgumentNullException(nameof(nodeData));
            return GetSyncStatus(nodeData.NodeType, nodeData.Config);
        }

        /// <summary>
        /// Evaluates synchronization status of a serialized LessonNodeData property.
        /// </summary>
        public static NodeSyncStatus GetSyncStatus(SerializedProperty nodeProperty)
        {
            if (nodeProperty == null) throw new ArgumentNullException(nameof(nodeProperty));

            var nodeTypeProp = nodeProperty.FindPropertyRelative("_nodeType");
            var configProp = nodeProperty.FindPropertyRelative("_config");

            if (nodeTypeProp == null || configProp == null)
            {
                return NodeSyncStatus.UnsupportedPhase2;
            }

            var nodeType = (NodeType)nodeTypeProp.enumValueIndex;
            var config = configProp.managedReferenceValue as INodeConfig;

            return GetSyncStatus(nodeType, config);
        }

        /// <summary>
        /// Returns true only when this exact managed-reference field owns a missing type entry.
        /// Unity reports managedReferenceId == -2 for missing types, so persisted LessonGraph assets
        /// require resolving the node's serialized RID before comparing it with Unity's missing list.
        /// </summary>
        public static bool HasMissingManagedReference(SerializedProperty configProperty)
        {
            if (configProperty == null || configProperty.serializedObject == null)
            {
                return false;
            }

            var target = configProperty.serializedObject.targetObject;
            if (target == null || !SerializationUtility.HasManagedReferencesWithMissingTypes(target))
            {
                return false;
            }

            var missingTypes = SerializationUtility.GetManagedReferencesWithMissingTypes(target);
            if (TryGetPersistedNodeConfigReferenceId(configProperty, out var referenceId))
            {
                for (var i = 0; i < missingTypes.Length; i++)
                {
                    if (missingTypes[i].referenceId == referenceId)
                    {
                        return true;
                    }
                }

                return false;
            }

            // A missing managed type is data that Unity cannot materialize. If exact resolution is
            // unavailable (for example a non-text asset), fail closed rather than auto-overwrite it.
            return configProperty.managedReferenceId == -2;
        }

        private static bool TryGetPersistedNodeConfigReferenceId(
            SerializedProperty configProperty,
            out long referenceId)
        {
            referenceId = default;

            var pathMatch = s_nodeConfigPathPattern.Match(configProperty.propertyPath);
            if (!pathMatch.Success || !int.TryParse(pathMatch.Groups[1].Value, out var nodeIndex))
            {
                return false;
            }

            var assetPath = AssetDatabase.GetAssetPath(configProperty.serializedObject.targetObject);
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
                return TryReadNodeConfigReferenceId(File.ReadAllLines(absolutePath), nodeIndex, out referenceId);
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

        private static bool TryReadNodeConfigReferenceId(
            IReadOnlyList<string> lines,
            int nodeIndex,
            out long referenceId)
        {
            referenceId = default;
            var nodesIndent = -1;
            var configIndex = -1;

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var trimmed = line.Trim();

                if (nodesIndent < 0)
                {
                    if (trimmed == "_nodes:")
                    {
                        nodesIndent = CountLeadingWhitespace(line);
                    }

                    continue;
                }

                if (trimmed.Length == 0)
                {
                    continue;
                }

                var indent = CountLeadingWhitespace(line);
                if (indent <= nodesIndent)
                {
                    break;
                }

                var inlineMatch = s_inlineConfigRidPattern.Match(trimmed);
                if (inlineMatch.Success)
                {
                    configIndex++;
                    if (configIndex == nodeIndex)
                    {
                        return long.TryParse(inlineMatch.Groups[1].Value, out referenceId);
                    }

                    continue;
                }

                if (trimmed != "_config:")
                {
                    continue;
                }

                configIndex++;
                if (configIndex != nodeIndex)
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
                    return ridMatch.Success && long.TryParse(ridMatch.Groups[1].Value, out referenceId);
                }

                return false;
            }

            return false;
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

        /// <summary>
        /// Initializes a null config for Phase 1 nodes with a matching default instance.
        /// Returns true if initialized, false if already non-null or unsupported.
        /// </summary>
        public static bool InitializeNullConfig(SerializedProperty nodeProperty)
        {
            if (nodeProperty == null) throw new ArgumentNullException(nameof(nodeProperty));
            if (nodeProperty.serializedObject == null) return false;

            nodeProperty.serializedObject.Update();

            var nodeTypeProp = nodeProperty.FindPropertyRelative("_nodeType");
            var configProp = nodeProperty.FindPropertyRelative("_config");

            if (nodeTypeProp == null || configProp == null) return false;

            var nodeType = (NodeType)nodeTypeProp.enumValueIndex;
            if (!IsSupportedPhase1Type(nodeType)) return false;

            if (configProp.managedReferenceValue != null) return false;

            if (nodeProperty.serializedObject.targetObject != null)
            {
                Undo.RecordObject(nodeProperty.serializedObject.targetObject, "Initialize Lesson Node Config");
            }

            configProp.managedReferenceValue = CreateDefaultConfig(nodeType);
            nodeProperty.serializedObject.ApplyModifiedProperties();
            return true;
        }

        /// <summary>
        /// Explicitly changes node type and replaces config with that type's default instance (Phase 1).
        /// If changing to Phase 2, updates discriminator without inventing a config.
        /// Preserves node ID and position.
        /// </summary>
        public static bool ChangeNodeType(SerializedProperty nodeProperty, NodeType newType)
        {
            if (nodeProperty == null) throw new ArgumentNullException(nameof(nodeProperty));
            if (nodeProperty.serializedObject == null) return false;

            nodeProperty.serializedObject.Update();

            var nodeTypeProp = nodeProperty.FindPropertyRelative("_nodeType");
            var configProp = nodeProperty.FindPropertyRelative("_config");

            if (nodeTypeProp == null || configProp == null) return false;

            if ((NodeType)nodeTypeProp.enumValueIndex == newType) return false;

            if (nodeProperty.serializedObject.targetObject != null)
            {
                Undo.RecordObject(nodeProperty.serializedObject.targetObject, "Change Lesson Node Type");
            }

            nodeTypeProp.enumValueIndex = (int)newType;
            if (IsSupportedPhase1Type(newType))
            {
                configProp.managedReferenceValue = CreateDefaultConfig(newType);
            }

            nodeProperty.serializedObject.ApplyModifiedProperties();
            return true;
        }

        /// <summary>
        /// Repairs a mismatch by changing the node discriminator to match the concrete config type,
        /// strictly preserving every field in the existing config payload.
        /// </summary>
        public static bool RepairUsingConfigType(SerializedProperty nodeProperty)
        {
            if (nodeProperty == null) throw new ArgumentNullException(nameof(nodeProperty));
            if (nodeProperty.serializedObject == null) return false;

            nodeProperty.serializedObject.Update();

            var nodeTypeProp = nodeProperty.FindPropertyRelative("_nodeType");
            var configProp = nodeProperty.FindPropertyRelative("_config");

            if (nodeTypeProp == null || configProp == null) return false;

            var configObj = configProp.managedReferenceValue;
            if (configObj == null) return false;

            if (!TryGetNodeTypeForConfigType(configObj.GetType(), out var matchingNodeType))
            {
                return false;
            }

            if (nodeProperty.serializedObject.targetObject != null)
            {
                Undo.RecordObject(nodeProperty.serializedObject.targetObject, "Repair Node By Config Type");
            }

            nodeTypeProp.enumValueIndex = (int)matchingNodeType;
            // config payload is untouched to preserve all existing data
            nodeProperty.serializedObject.ApplyModifiedProperties();
            return true;
        }

        /// <summary>
        /// Repairs a mismatch by replacing the config payload with a default instance matching
        /// the current node discriminator (destructive payload replacement).
        /// </summary>
        public static bool RepairUsingNodeType(SerializedProperty nodeProperty)
        {
            if (nodeProperty == null) throw new ArgumentNullException(nameof(nodeProperty));
            if (nodeProperty.serializedObject == null) return false;

            nodeProperty.serializedObject.Update();

            var nodeTypeProp = nodeProperty.FindPropertyRelative("_nodeType");
            var configProp = nodeProperty.FindPropertyRelative("_config");

            if (nodeTypeProp == null || configProp == null) return false;
            if (GetSyncStatus(nodeProperty) != NodeSyncStatus.Mismatch) return false;

            var nodeType = (NodeType)nodeTypeProp.enumValueIndex;
            if (!IsSupportedPhase1Type(nodeType)) return false;

            if (nodeProperty.serializedObject.targetObject != null)
            {
                Undo.RecordObject(nodeProperty.serializedObject.targetObject, "Repair Node By Node Type");
            }

            configProp.managedReferenceValue = CreateDefaultConfig(nodeType);
            nodeProperty.serializedObject.ApplyModifiedProperties();
            return true;
        }
    }

    /// <summary>
    /// UI Toolkit PropertyDrawer for LessonNodeData.
    /// Synchronizes _nodeType with polymorphic [SerializeReference] _config,
    /// alerts authors to legacy/imported mismatches with data-preserving repair choices,
    /// and flags Phase 2 node types as unsupported in Phase 1.
    /// </summary>
    [CustomPropertyDrawer(typeof(LessonNodeData))]
    public sealed class LessonNodeDataDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            root.name = "lesson-node-data-drawer";

            if (property == null || property.serializedObject == null)
            {
                return root;
            }

            if (property.serializedObject.isEditingMultipleObjects)
            {
                var warning = new HelpBox(
                    "Editing multiple LessonGraph nodes at once is not supported. Select one graph asset to edit node data safely.",
                    HelpBoxMessageType.Warning);
                warning.name = "multi-edit-warning";
                root.Add(warning);
                return root;
            }

            var nodeTypeProp = property.FindPropertyRelative("_nodeType");
            var configProp = property.FindPropertyRelative("_config");

            if (nodeTypeProp == null || configProp == null)
            {
                return root;
            }

            // Auto-reconcile only genuinely null payloads. Missing managed-reference types retain
            // serialized recovery data and must never be mistaken for an empty config.
            if (!LessonNodeSyncHelper.HasMissingManagedReference(configProp) &&
                LessonNodeSyncHelper.GetSyncStatus(property) == NodeSyncStatus.NullConfig)
            {
                LessonNodeSyncHelper.InitializeNullConfig(property);
            }

            Rebuild(root, property);

            TrackProperty(root, nodeTypeProp, property);
            var idProp = property.FindPropertyRelative("_id");
            if (idProp != null)
            {
                TrackNodeIdProperty(root, idProp, property);
            }
            TrackProperty(root, configProp, property);

            return root;
        }

        private static void TrackProperty(
            VisualElement root,
            SerializedProperty trackedProperty,
            SerializedProperty nodeProperty)
        {
            var tracker = new VisualElement();
            tracker.TrackPropertyValue(trackedProperty, _ => Rebuild(root, nodeProperty));
            root.Add(tracker);
        }

        private static void TrackNodeIdProperty(
            VisualElement root,
            SerializedProperty idProperty,
            SerializedProperty nodeProperty)
        {
            var tracker = new VisualElement();
            tracker.TrackPropertyValue(idProperty, _ => RefreshNodeIdControls(root, nodeProperty));
            root.Add(tracker);
        }

        private static void RefreshNodeIdControls(VisualElement root, SerializedProperty nodeProperty)
        {
            var idProperty = nodeProperty?.FindPropertyRelative("_id");
            var idField = root?.Q<PropertyField>("id-field");
            if (idProperty == null || idField?.parent == null)
            {
                return;
            }

            var generateButton = root.Q<Button>("generate-node-id-button");
            var needsGenerateButton = string.IsNullOrWhiteSpace(idProperty.stringValue);
            if (needsGenerateButton && generateButton == null)
            {
                var button = CreateGenerateNodeIdButton(root, nodeProperty);
                idField.parent.Insert(idField.parent.IndexOf(idField) + 1, button);
            }
            else if (!needsGenerateButton)
            {
                generateButton?.RemoveFromHierarchy();
            }
        }

        private static Button CreateGenerateNodeIdButton(
            VisualElement root,
            SerializedProperty nodeProperty)
        {
            var button = new Button(() =>
            {
                var serializedObject = nodeProperty?.serializedObject;
                if (serializedObject == null || serializedObject.targetObject == null)
                {
                    return;
                }

                serializedObject.Update();
                var currentIdProperty = nodeProperty.FindPropertyRelative("_id");
                if (currentIdProperty == null || !string.IsNullOrWhiteSpace(currentIdProperty.stringValue))
                {
                    Rebuild(root, nodeProperty);
                    return;
                }

                Undo.RecordObject(serializedObject.targetObject, "Generate Lesson Node ID");
                currentIdProperty.stringValue = Guid.NewGuid().ToString("N");
                serializedObject.ApplyModifiedProperties();
                Rebuild(root, nodeProperty);
            })
            {
                text = "Generate Node ID",
                tooltip = "Assign a new stable ID to this node."
            };
            button.name = "generate-node-id-button";
            return button;
        }

        /// <summary>
        /// Rebuilds the visual tree for the drawer container.
        /// </summary>
        public static void Rebuild(VisualElement root, SerializedProperty property)
        {
            var content = root.Q<VisualElement>("node-data-content");
            if (content == null)
            {
                content = new VisualElement { name = "node-data-content" };
                root.Add(content);
            }

            content.Unbind();
            content.Clear();

            if (property == null || property.serializedObject == null)
            {
                return;
            }

            var idProp = property.FindPropertyRelative("_id");
            var nodeTypeProp = property.FindPropertyRelative("_nodeType");
            var posProp = property.FindPropertyRelative("_position");
            var configProp = property.FindPropertyRelative("_config");

            if (nodeTypeProp == null || configProp == null)
            {
                return;
            }

            var container = new VisualElement();
            container.style.marginBottom = 6;
            container.style.paddingTop = 4;
            container.style.paddingBottom = 4;
            container.style.paddingLeft = 4;
            container.style.paddingRight = 4;
            container.style.borderTopWidth = 1;
            container.style.borderBottomWidth = 1;
            container.style.borderLeftWidth = 1;
            container.style.borderRightWidth = 1;
            container.style.borderTopColor = new StyleColor(new Color(0.25f, 0.25f, 0.25f, 0.5f));
            container.style.borderBottomColor = new StyleColor(new Color(0.25f, 0.25f, 0.25f, 0.5f));
            container.style.borderLeftColor = new StyleColor(new Color(0.25f, 0.25f, 0.25f, 0.5f));
            container.style.borderRightColor = new StyleColor(new Color(0.25f, 0.25f, 0.25f, 0.5f));
            container.style.borderTopLeftRadius = 4;
            container.style.borderTopRightRadius = 4;
            container.style.borderBottomLeftRadius = 4;
            container.style.borderBottomRightRadius = 4;

            // 1. Node ID
            if (idProp != null)
            {
                var idField = new PropertyField(idProp, "Node ID");
                idField.name = "id-field";
                container.Add(idField);

                if (string.IsNullOrWhiteSpace(idProp.stringValue))
                {
                    container.Add(CreateGenerateNodeIdButton(root, property));
                }
            }

            // 2. Node Type Dropdown
            var currentType = (NodeType)nodeTypeProp.enumValueIndex;
            var nodeTypeField = new EnumField("Node Type", currentType);
            nodeTypeField.name = "node-type-field";
            Action<NodeType> onTypeChanged = newType =>
            {
                if (newType == currentType)
                {
                    return;
                }

                var missingConfig = LessonNodeSyncHelper.HasMissingManagedReference(configProp);
                var replacingConfig = LessonNodeSyncHelper.IsSupportedPhase1Type(newType) &&
                    (configProp.managedReferenceValue != null || missingConfig);
                if (replacingConfig)
                {
                    nodeTypeField.SetValueWithoutNotify(currentType);
                    root.Q<VisualElement>("node-type-change-confirmation")?.RemoveFromHierarchy();

                    var confirmation = new VisualElement();
                    confirmation.name = "node-type-change-confirmation";
                    var warning = new HelpBox(
                        $"Changing Node Type to '{newType}' will replace the current config and discard its values. Confirm to continue.",
                        HelpBoxMessageType.Warning);
                    warning.name = "node-type-change-warning";
                    confirmation.Add(warning);

                    var buttons = new VisualElement();
                    buttons.style.flexDirection = FlexDirection.Row;
                    var confirmButton = new Button(() =>
                    {
                        LessonNodeSyncHelper.ChangeNodeType(property, newType);
                        Rebuild(root, property);
                    })
                    {
                        text = "Confirm Type Change",
                        tooltip = "Replace the current node config with the selected type's default config."
                    };
                    confirmButton.name = "confirm-node-type-change-button";
                    buttons.Add(confirmButton);

                    var cancelButton = new Button(() => Rebuild(root, property))
                    {
                        text = "Cancel",
                        tooltip = "Keep the current node type and config."
                    };
                    cancelButton.name = "cancel-node-type-change-button";
                    buttons.Add(cancelButton);
                    confirmation.Add(buttons);
                    content.Add(confirmation);
                    return;
                }

                LessonNodeSyncHelper.ChangeNodeType(property, newType);
                Rebuild(root, property);
            };
            nodeTypeField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue == null || evt.newValue.Equals(evt.previousValue)) return;
                onTypeChanged((NodeType)evt.newValue);
            });
            container.Add(nodeTypeField);

            // 3. Position
            if (posProp != null)
            {
                var posField = new PropertyField(posProp, "Position");
                posField.name = "position-field";
                container.Add(posField);
            }

            // 4. Status, Warnings, and Repair
            var status = LessonNodeSyncHelper.GetSyncStatus(property);
            var configObj = configProp.managedReferenceValue as INodeConfig;
            var configType = configObj?.GetType();

            if (LessonNodeSyncHelper.HasMissingManagedReference(configProp))
            {
                var missingReferenceBox = new HelpBox(
                    "This node config has a missing managed-reference type. Its serialized payload " +
                    "is preserved; restore the missing type or migrate the asset before editing this config.",
                    HelpBoxMessageType.Error);
                missingReferenceBox.name = "missing-reference-warning";
                container.Add(missingReferenceBox);
            }
            else if (status == NodeSyncStatus.UnsupportedPhase2)
            {
                var phase2Box = new HelpBox(
                    $"Phase 2 node type '{currentType}' is not supported in Phase 1 graphs. " +
                    "Serialized data is preserved. Run LessonGraphValidator to check the graph.",
                    HelpBoxMessageType.Warning);
                phase2Box.name = "phase2-warning";
                container.Add(phase2Box);

                if (configObj != null)
                {
                    var configField = new PropertyField(configProp, $"Config ({configType.Name})");
                    configField.name = "config-field";
                    container.Add(configField);
                }
            }
            else if (status == NodeSyncStatus.Mismatch)
            {
                string configTypeName = configType != null ? configType.Name : "None";
                var warningBox = new HelpBox(
                    $"Mismatch detected: Node Type is '{currentType}' but Config is '{configTypeName}'. Choose a repair option below:",
                    HelpBoxMessageType.Warning);
                warningBox.name = "mismatch-warning";
                container.Add(warningBox);

                var repairButtons = new VisualElement();
                repairButtons.name = "repair-buttons";
                repairButtons.style.flexDirection = FlexDirection.Row;
                repairButtons.style.flexWrap = Wrap.Wrap;
                repairButtons.style.marginTop = 3;
                repairButtons.style.marginBottom = 5;

                // Option A: Use Config Type (Preserve Data)
                if (configType != null && LessonNodeSyncHelper.TryGetNodeTypeForConfigType(configType, out var matchingNodeType))
                {
                    Action onUseConfigClick = () =>
                    {
                        LessonNodeSyncHelper.RepairUsingConfigType(property);
                        Rebuild(root, property);
                    };
                    var useConfigBtn = new Button(onUseConfigClick)
                    {
                        text = $"Use Config Type ({matchingNodeType})",
                        tooltip = $"Change discriminator to {matchingNodeType} and preserve all existing config fields."
                    };
                    useConfigBtn.name = "use-config-type-button";
                    useConfigBtn.style.marginRight = 6;
                    repairButtons.Add(useConfigBtn);
                }

                // Option B: Use Node Type (Destructive payload reset)
                Action onUseNodeClick = () =>
                {
                    LessonNodeSyncHelper.RepairUsingNodeType(property);
                    Rebuild(root, property);
                };
                var useNodeBtn = new Button(onUseNodeClick)
                {
                    text = $"Use Node Type ({currentType}) [Replace Payload]",
                    tooltip = $"Replace payload with a default {currentType} config (destructive)."
                };
                useNodeBtn.name = "use-node-type-button";
                repairButtons.Add(useNodeBtn);

                container.Add(repairButtons);

                var configField = new PropertyField(configProp, $"Mismatched Config ({configTypeName})");
                configField.name = "config-field";
                container.Add(configField);
            }
            else
            {
                var configField = new PropertyField(configProp, "Config");
                configField.name = "config-field";
                container.Add(configField);
            }

            content.Add(container);
            if (root.panel != null)
            {
                content.Bind(property.serializedObject);
            }
        }
    }
}
