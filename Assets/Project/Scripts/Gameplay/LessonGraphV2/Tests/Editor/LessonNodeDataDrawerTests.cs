using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Editor;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    [TestFixture]
    public sealed class LessonNodeDataDrawerTests
    {
        private LessonGraph _graph;
        private SerializedObject _serializedObject;
        private EditorWindow _window;
        private string _tempAssetPath;

        [SetUp]
        public void SetUp()
        {
            _graph = ScriptableObject.CreateInstance<LessonGraph>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                {
                    LogAssert.Expect(LogType.Error, "No graphic device is available to initialize the view.");
                    LogAssert.Expect(LogType.Error, "No graphic device is available to initialize the view.");
                    LogAssert.Expect(LogType.Error, "No graphic device is available to initialize the view.");
                    LogAssert.Expect(LogType.Error, "No graphic device is available to initialize the view.");
                }

                _window.Close();
            }

            if (_graph != null)
            {
                Undo.ClearUndo(_graph);
            }

            if (!string.IsNullOrEmpty(_tempAssetPath))
            {
                AssetDatabase.DeleteAsset(_tempAssetPath);
                _graph = null;
            }

            if (_graph != null)
            {
                UnityEngine.Object.DestroyImmediate(_graph);
            }
        }

        private SerializedProperty SetupNodeProperty(LessonNodeData node)
        {
            _graph.Editor_SetNodes(new List<LessonNodeData> { node });
            _serializedObject = new SerializedObject(_graph);
            var nodesProp = _serializedObject.FindProperty("_nodes");
            return nodesProp.GetArrayElementAtIndex(0);
        }

        private static void Submit(Button button)
        {
            button.Focus();
            using (var submitEvent = NavigationSubmitEvent.GetPooled())
            {
                button.SendEvent(submitEvent);
            }
        }

        private VisualElement AttachAndBind(VisualElement root)
        {
            _window = ScriptableObject.CreateInstance<EditorWindow>();
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                LogAssert.Expect(LogType.Error, "No graphic device is available to initialize the view.");
                LogAssert.Expect(LogType.Error, "No graphic device is available to show the window.");
                LogAssert.Expect(LogType.Error, "No graphic device is available to initialize the view.");
                LogAssert.Expect(LogType.Error, "No graphic device is available to show the window.");
            }

            _window.Show();
            _window.rootVisualElement.Add(root);
            root.Bind(_serializedObject);
            return root;
        }

        // ── 1. Phase 1 Mapping ──────────────────────────────────────────────────

        [TestCase(NodeType.Quest, ExpectedResult = true)]
        [TestCase(NodeType.Dialogue, ExpectedResult = true)]
        [TestCase(NodeType.Wait, ExpectedResult = true)]
        [TestCase(NodeType.Checkpoint, ExpectedResult = true)]
        public bool IsSupportedPhase1Type_ForPhase1Types_ReturnsTrue(NodeType nodeType)
        {
            return LessonNodeSyncHelper.IsSupportedPhase1Type(nodeType);
        }

        [TestCase(NodeType.Timeline, ExpectedResult = false)]
        [TestCase(NodeType.Parallel, ExpectedResult = false)]
        [TestCase(NodeType.Gate, ExpectedResult = false)]
        [TestCase((NodeType)999, ExpectedResult = false)]
        public bool IsSupportedPhase1Type_ForPhase2OrUnknownTypes_ReturnsFalse(NodeType nodeType)
        {
            return LessonNodeSyncHelper.IsSupportedPhase1Type(nodeType);
        }

        [Test]
        public void GetExpectedConfigType_ForPhase1Types_ReturnsCorrectType()
        {
            Assert.AreEqual(typeof(QuestNodeConfig), LessonNodeSyncHelper.GetExpectedConfigType(NodeType.Quest));
            Assert.AreEqual(typeof(DialogueNodeConfig), LessonNodeSyncHelper.GetExpectedConfigType(NodeType.Dialogue));
            Assert.AreEqual(typeof(WaitNodeConfig), LessonNodeSyncHelper.GetExpectedConfigType(NodeType.Wait));
            Assert.AreEqual(typeof(CheckpointNodeConfig), LessonNodeSyncHelper.GetExpectedConfigType(NodeType.Checkpoint));
        }

        [TestCase(NodeType.Timeline)]
        [TestCase(NodeType.Parallel)]
        [TestCase(NodeType.Gate)]
        public void GetExpectedConfigType_ForPhase2Types_ReturnsNull(NodeType phase2Type)
        {
            Assert.IsNull(LessonNodeSyncHelper.GetExpectedConfigType(phase2Type));
        }

        [Test]
        public void TryGetNodeTypeForConfigType_ForPhase1Configs_ReturnsCorrectNodeType()
        {
            Assert.IsTrue(LessonNodeSyncHelper.TryGetNodeTypeForConfigType(typeof(QuestNodeConfig), out var questType));
            Assert.AreEqual(NodeType.Quest, questType);

            Assert.IsTrue(LessonNodeSyncHelper.TryGetNodeTypeForConfigType(typeof(DialogueNodeConfig), out var dialogueType));
            Assert.AreEqual(NodeType.Dialogue, dialogueType);

            Assert.IsTrue(LessonNodeSyncHelper.TryGetNodeTypeForConfigType(typeof(WaitNodeConfig), out var waitType));
            Assert.AreEqual(NodeType.Wait, waitType);

            Assert.IsTrue(LessonNodeSyncHelper.TryGetNodeTypeForConfigType(typeof(CheckpointNodeConfig), out var checkpointType));
            Assert.AreEqual(NodeType.Checkpoint, checkpointType);
        }

        [Test]
        public void TryGetNodeTypeForConfigType_ForNonPhase1Configs_ReturnsFalse()
        {
            Assert.IsFalse(LessonNodeSyncHelper.TryGetNodeTypeForConfigType(typeof(object), out _));
            Assert.IsFalse(LessonNodeSyncHelper.TryGetNodeTypeForConfigType(typeof(string), out _));
            Assert.IsFalse(LessonNodeSyncHelper.TryGetNodeTypeForConfigType(null, out _));
        }

        [Test]
        public void CreateDefaultConfig_ForPhase1Types_CreatesValidInstance()
        {
            var questCfg = LessonNodeSyncHelper.CreateDefaultConfig(NodeType.Quest);
            Assert.IsInstanceOf<QuestNodeConfig>(questCfg);

            var dialogueCfg = LessonNodeSyncHelper.CreateDefaultConfig(NodeType.Dialogue);
            Assert.IsInstanceOf<DialogueNodeConfig>(dialogueCfg);

            var waitCfg = LessonNodeSyncHelper.CreateDefaultConfig(NodeType.Wait);
            Assert.IsInstanceOf<WaitNodeConfig>(waitCfg);

            var checkpointCfg = LessonNodeSyncHelper.CreateDefaultConfig(NodeType.Checkpoint);
            Assert.IsInstanceOf<CheckpointNodeConfig>(checkpointCfg);
        }

        [TestCase(NodeType.Timeline)]
        [TestCase(NodeType.Parallel)]
        [TestCase(NodeType.Gate)]
        public void CreateDefaultConfig_ForPhase2Types_ThrowsNotSupportedException(NodeType phase2Type)
        {
            Assert.Throws<NotSupportedException>(() => LessonNodeSyncHelper.CreateDefaultConfig(phase2Type));
        }

        // ── 2. Mismatch Detection ───────────────────────────────────────────────

        [Test]
        public void GetSyncStatus_WhenTypeAndConfigMatch_ReturnsCompatible()
        {
            Assert.AreEqual(NodeSyncStatus.Compatible,
                LessonNodeSyncHelper.GetSyncStatus(NodeType.Quest, new QuestNodeConfig()));
            Assert.AreEqual(NodeSyncStatus.Compatible,
                LessonNodeSyncHelper.GetSyncStatus(NodeType.Dialogue, new DialogueNodeConfig()));
            Assert.AreEqual(NodeSyncStatus.Compatible,
                LessonNodeSyncHelper.GetSyncStatus(NodeType.Wait, new WaitNodeConfig()));
            Assert.AreEqual(NodeSyncStatus.Compatible,
                LessonNodeSyncHelper.GetSyncStatus(NodeType.Checkpoint, new CheckpointNodeConfig()));
        }

        [Test]
        public void GetSyncStatus_WhenTypeAndConfigMismatch_ReturnsMismatch()
        {
            Assert.AreEqual(NodeSyncStatus.Mismatch,
                LessonNodeSyncHelper.GetSyncStatus(NodeType.Quest, new DialogueNodeConfig()));
            Assert.AreEqual(NodeSyncStatus.Mismatch,
                LessonNodeSyncHelper.GetSyncStatus(NodeType.Dialogue, new QuestNodeConfig()));
            Assert.AreEqual(NodeSyncStatus.Mismatch,
                LessonNodeSyncHelper.GetSyncStatus(NodeType.Wait, new CheckpointNodeConfig()));
            Assert.AreEqual(NodeSyncStatus.Mismatch,
                LessonNodeSyncHelper.GetSyncStatus(NodeType.Checkpoint, new WaitNodeConfig()));
        }

        [Test]
        public void GetSyncStatus_WhenPhase1AndConfigNull_ReturnsNullConfig()
        {
            Assert.AreEqual(NodeSyncStatus.NullConfig, LessonNodeSyncHelper.GetSyncStatus(NodeType.Quest, null));
            Assert.AreEqual(NodeSyncStatus.NullConfig, LessonNodeSyncHelper.GetSyncStatus(NodeType.Dialogue, null));
            Assert.AreEqual(NodeSyncStatus.NullConfig, LessonNodeSyncHelper.GetSyncStatus(NodeType.Wait, null));
            Assert.AreEqual(NodeSyncStatus.NullConfig, LessonNodeSyncHelper.GetSyncStatus(NodeType.Checkpoint, null));
        }

        [TestCase(NodeType.Timeline)]
        [TestCase(NodeType.Parallel)]
        [TestCase(NodeType.Gate)]
        public void GetSyncStatus_WhenPhase2Type_ReturnsUnsupportedPhase2(NodeType phase2Type)
        {
            Assert.AreEqual(NodeSyncStatus.UnsupportedPhase2, LessonNodeSyncHelper.GetSyncStatus(phase2Type, null));
            Assert.AreEqual(NodeSyncStatus.UnsupportedPhase2, LessonNodeSyncHelper.GetSyncStatus(phase2Type, new QuestNodeConfig()));
        }

        [Test]
        public void GetSyncStatus_FromSerializedProperty_DetectsStatusCorrectly()
        {
            var nodeProp = SetupNodeProperty(new LessonNodeData("node_1", NodeType.Quest, new DialogueNodeConfig()));
            Assert.AreEqual(NodeSyncStatus.Mismatch, LessonNodeSyncHelper.GetSyncStatus(nodeProp));
        }

        // ── 3. Null Initialization ──────────────────────────────────────────────

        [Test]
        public void InitializeNullConfig_WhenNullConfigPhase1_SetsDefaultConfigAndPreservesIdentity()
        {
            var nodeProp = SetupNodeProperty(new LessonNodeData("guid-123", NodeType.Quest, null, new Vector2(100f, 200f)));

            bool result = LessonNodeSyncHelper.InitializeNullConfig(nodeProp);

            Assert.IsTrue(result);
            Assert.AreEqual("guid-123", nodeProp.FindPropertyRelative("_id").stringValue);
            Assert.AreEqual(new Vector2(100f, 200f), nodeProp.FindPropertyRelative("_position").vector2Value);
            Assert.AreEqual((int)NodeType.Quest, nodeProp.FindPropertyRelative("_nodeType").enumValueIndex);

            var config = nodeProp.FindPropertyRelative("_config").managedReferenceValue;
            Assert.IsNotNull(config);
            Assert.IsInstanceOf<QuestNodeConfig>(config);
        }

        [Test]
        public void InitializeNullConfig_WhenConfigAlreadyNonNull_ReturnsFalseAndPreservesConfig()
        {
            var existingConfig = new QuestNodeConfig(new List<string> { "binding_1" }, 10f);
            var nodeProp = SetupNodeProperty(new LessonNodeData("guid-123", NodeType.Quest, existingConfig));

            bool result = LessonNodeSyncHelper.InitializeNullConfig(nodeProp);

            Assert.IsFalse(result);
            Assert.AreSame(existingConfig, nodeProp.FindPropertyRelative("_config").managedReferenceValue);
        }

        [TestCase(NodeType.Timeline)]
        [TestCase(NodeType.Parallel)]
        [TestCase(NodeType.Gate)]
        public void InitializeNullConfig_WhenPhase2_ReturnsFalseAndKeepsNull(NodeType phase2Type)
        {
            var nodeProp = SetupNodeProperty(new LessonNodeData("guid-p2", phase2Type, null));

            bool result = LessonNodeSyncHelper.InitializeNullConfig(nodeProp);

            Assert.IsFalse(result);
            Assert.IsNull(nodeProp.FindPropertyRelative("_config").managedReferenceValue);
        }

        // ── 4. Replacement & Node Type Change ───────────────────────────────────

        [Test]
        public void ChangeNodeType_FromQuestToDialogue_ReplacesConfigWithDefaultDialogueConfig()
        {
            var nodeProp = SetupNodeProperty(new LessonNodeData("guid-rep", NodeType.Quest, new QuestNodeConfig(), new Vector2(50f, 75f)));

            bool result = LessonNodeSyncHelper.ChangeNodeType(nodeProp, NodeType.Dialogue);

            Assert.IsTrue(result);
            Assert.AreEqual("guid-rep", nodeProp.FindPropertyRelative("_id").stringValue);
            Assert.AreEqual(new Vector2(50f, 75f), nodeProp.FindPropertyRelative("_position").vector2Value);
            Assert.AreEqual((int)NodeType.Dialogue, nodeProp.FindPropertyRelative("_nodeType").enumValueIndex);

            var config = nodeProp.FindPropertyRelative("_config").managedReferenceValue;
            Assert.IsNotNull(config);
            Assert.IsInstanceOf<DialogueNodeConfig>(config);
        }

        [Test]
        public void ChangeNodeType_FromDialogueToWait_ReplacesConfigWithDefaultWaitConfig()
        {
            var nodeProp = SetupNodeProperty(new LessonNodeData("guid-wait", NodeType.Dialogue, new DialogueNodeConfig()));

            bool result = LessonNodeSyncHelper.ChangeNodeType(nodeProp, NodeType.Wait);

            Assert.IsTrue(result);
            Assert.AreEqual((int)NodeType.Wait, nodeProp.FindPropertyRelative("_nodeType").enumValueIndex);
            Assert.IsInstanceOf<WaitNodeConfig>(nodeProp.FindPropertyRelative("_config").managedReferenceValue);
        }

        [Test]
        public void ChangeNodeType_WhenTypeAlreadyMatches_PreservesPopulatedConfigAndReturnsFalse()
        {
            var originalConfig = new QuestNodeConfig(
                new List<string> { "binding-preserved" }, 42f, "Keep this prompt");
            var nodeProp = SetupNodeProperty(
                new LessonNodeData("guid-same-type", NodeType.Quest, originalConfig));

            bool changed = LessonNodeSyncHelper.ChangeNodeType(nodeProp, NodeType.Quest);

            Assert.IsFalse(changed);
            Assert.AreSame(
                originalConfig,
                nodeProp.FindPropertyRelative("_config").managedReferenceValue);
        }

        [Test]
        public void ChangeNodeType_ToPhase2_SetsNodeTypeWithoutInventingConfig()
        {
            var nodeProp = SetupNodeProperty(new LessonNodeData("guid-p2", NodeType.Quest, null));

            bool result = LessonNodeSyncHelper.ChangeNodeType(nodeProp, NodeType.Timeline);

            Assert.IsTrue(result);
            Assert.AreEqual((int)NodeType.Timeline, nodeProp.FindPropertyRelative("_nodeType").enumValueIndex);
            Assert.IsNull(nodeProp.FindPropertyRelative("_config").managedReferenceValue);
        }

        // ── 5. Data-Preserving Config Repair ─────────────────────────────────────

        [Test]
        public void RepairUsingConfigType_MismatchedQuestWithDialogueConfig_ChangesNodeTypeToDialogueAndPreservesAllDialogueFields()
        {
            var populatedDialogue = new DialogueNodeConfig(
                "seq_bathroom_01",
                "Please wash your hands.",
                false,
                18.5f,
                "npc_therapist");

            var nodeProp = SetupNodeProperty(new LessonNodeData("guid-mismatch", NodeType.Quest, populatedDialogue, new Vector2(12f, 34f)));

            Assert.AreEqual(NodeSyncStatus.Mismatch, LessonNodeSyncHelper.GetSyncStatus(nodeProp));

            bool repaired = LessonNodeSyncHelper.RepairUsingConfigType(nodeProp);

            Assert.IsTrue(repaired);
            Assert.AreEqual("guid-mismatch", nodeProp.FindPropertyRelative("_id").stringValue);
            Assert.AreEqual(new Vector2(12f, 34f), nodeProp.FindPropertyRelative("_position").vector2Value);
            Assert.AreEqual((int)NodeType.Dialogue, nodeProp.FindPropertyRelative("_nodeType").enumValueIndex);

            var config = nodeProp.FindPropertyRelative("_config").managedReferenceValue as DialogueNodeConfig;
            Assert.IsNotNull(config);
            Assert.AreEqual("seq_bathroom_01", config.SequenceId);
            Assert.AreEqual("Please wash your hands.", config.Text);
            Assert.AreEqual("npc_therapist", config.NpcBindingId);
            Assert.IsFalse(config.Blocking);
            Assert.AreEqual(18.5f, config.TimeoutSeconds);
            Assert.AreEqual(NodeSyncStatus.Compatible, LessonNodeSyncHelper.GetSyncStatus(nodeProp));
        }

        [Test]
        public void RepairUsingConfigType_WhenConfigIsNull_ReturnsFalse()
        {
            var nodeProp = SetupNodeProperty(new LessonNodeData("guid-null", NodeType.Quest, null));

            bool repaired = LessonNodeSyncHelper.RepairUsingConfigType(nodeProp);

            Assert.IsFalse(repaired);
            Assert.AreEqual((int)NodeType.Quest, nodeProp.FindPropertyRelative("_nodeType").enumValueIndex);
        }

        // ── 6. Repair Using Node Type ───────────────────────────────────────────

        [Test]
        public void RepairUsingNodeType_MismatchedDialogueWithQuestConfig_ReplacesWithDefaultDialogueConfig()
        {
            var questConfig = new QuestNodeConfig(new List<string> { "old_binding" }, 99f, "Old prompt");
            var nodeProp = SetupNodeProperty(new LessonNodeData("guid-repair-node", NodeType.Dialogue, questConfig, new Vector2(1f, 2f)));

            Assert.AreEqual(NodeSyncStatus.Mismatch, LessonNodeSyncHelper.GetSyncStatus(nodeProp));

            bool repaired = LessonNodeSyncHelper.RepairUsingNodeType(nodeProp);

            Assert.IsTrue(repaired);
            Assert.AreEqual("guid-repair-node", nodeProp.FindPropertyRelative("_id").stringValue);
            Assert.AreEqual(new Vector2(1f, 2f), nodeProp.FindPropertyRelative("_position").vector2Value);
            Assert.AreEqual((int)NodeType.Dialogue, nodeProp.FindPropertyRelative("_nodeType").enumValueIndex);

            var config = nodeProp.FindPropertyRelative("_config").managedReferenceValue;
            Assert.IsNotNull(config);
            Assert.IsInstanceOf<DialogueNodeConfig>(config);
            Assert.AreEqual(NodeSyncStatus.Compatible, LessonNodeSyncHelper.GetSyncStatus(nodeProp));
        }

        [Test]
        public void RepairUsingNodeType_WhenNodeIsAlreadyCompatible_PreservesConfigAndReturnsFalse()
        {
            var originalConfig = new DialogueNodeConfig(
                "seq-compatible", "Keep this dialogue", "teacher-npc", false, 12f);
            var nodeProp = SetupNodeProperty(
                new LessonNodeData("guid-compatible", NodeType.Dialogue, originalConfig));

            bool repaired = LessonNodeSyncHelper.RepairUsingNodeType(nodeProp);

            Assert.IsFalse(repaired);
            Assert.AreSame(
                originalConfig,
                nodeProp.FindPropertyRelative("_config").managedReferenceValue);
        }

        [TestCase(NodeType.Timeline)]
        [TestCase(NodeType.Parallel)]
        [TestCase(NodeType.Gate)]
        public void RepairUsingNodeType_WhenPhase2_ReturnsFalse(NodeType phase2Type)
        {
            var nodeProp = SetupNodeProperty(new LessonNodeData("guid-p2", phase2Type, new QuestNodeConfig()));

            bool repaired = LessonNodeSyncHelper.RepairUsingNodeType(nodeProp);

            Assert.IsFalse(repaired);
        }

        // ── 7. Undo-Safe Serialized Mutation ────────────────────────────────────

        [Test]
        public void Undo_AfterChangeNodeType_RestoresPreviousNodeTypeAndConfig()
        {
            var initialConfig = new QuestNodeConfig(new List<string> { "initial_bid" }, 25f);
            var nodeProp = SetupNodeProperty(new LessonNodeData("guid-undo-1", NodeType.Quest, initialConfig));

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Test Change Node Type");

            LessonNodeSyncHelper.ChangeNodeType(nodeProp, NodeType.Wait);

            Assert.AreEqual((int)NodeType.Wait, nodeProp.FindPropertyRelative("_nodeType").enumValueIndex);
            Assert.IsInstanceOf<WaitNodeConfig>(nodeProp.FindPropertyRelative("_config").managedReferenceValue);

            Undo.PerformUndo();
            _serializedObject.Update();

            Assert.AreEqual((int)NodeType.Quest, nodeProp.FindPropertyRelative("_nodeType").enumValueIndex);
            var restoredConfig = nodeProp.FindPropertyRelative("_config").managedReferenceValue as QuestNodeConfig;
            Assert.IsNotNull(restoredConfig);
            Assert.AreEqual(25f, restoredConfig.TimeoutSeconds);
        }

        [Test]
        public void Undo_AfterRepairUsingConfigType_RestoresPreviousNodeType()
        {
            var dialogueConfig = new DialogueNodeConfig("seq_undo", "Undo dialogue text", blocking: true, timeoutSeconds: 10f);
            var nodeProp = SetupNodeProperty(new LessonNodeData("guid-undo-2", NodeType.Quest, dialogueConfig));

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Test Repair By Config Type");

            LessonNodeSyncHelper.RepairUsingConfigType(nodeProp);

            Assert.AreEqual((int)NodeType.Dialogue, nodeProp.FindPropertyRelative("_nodeType").enumValueIndex);

            Undo.PerformUndo();
            _serializedObject.Update();

            Assert.AreEqual((int)NodeType.Quest, nodeProp.FindPropertyRelative("_nodeType").enumValueIndex);
            var restoredConfig = nodeProp.FindPropertyRelative("_config").managedReferenceValue as DialogueNodeConfig;
            Assert.IsNotNull(restoredConfig);
            Assert.AreEqual("seq_undo", restoredConfig.SequenceId);
            Assert.AreEqual("Undo dialogue text", restoredConfig.Text);
        }

        // ── 8. Drawer Visual Tree (UI Toolkit) ──────────────────────────────────

        [Test]
        public void CreatePropertyGUI_CompatibleNode_RendersFieldsAndNoWarning()
        {
            var nodeProp = SetupNodeProperty(new LessonNodeData("guid-ui-1", NodeType.Quest, new QuestNodeConfig()));
            var drawer = new LessonNodeDataDrawer();

            var root = drawer.CreatePropertyGUI(nodeProp);

            Assert.IsNotNull(root);
            Assert.IsNull(root.Q<HelpBox>("mismatch-warning"));
            Assert.IsNull(root.Q<HelpBox>("phase2-warning"));
            Assert.IsNull(root.Q<VisualElement>("repair-buttons"));

            Assert.IsNotNull(root.Q<PropertyField>("id-field"));
            Assert.IsNotNull(root.Q<EnumField>("node-type-field"));
            Assert.IsNotNull(root.Q<PropertyField>("position-field"));
            Assert.IsNotNull(root.Q<PropertyField>("config-field"));
        }

        [Test]
        public void CreatePropertyGUI_NodeTypeField_IsBoundToSerializedNodeType()
        {
            var nodeProp = SetupNodeProperty(
                new LessonNodeData("guid-ui-bound", NodeType.Quest, new QuestNodeConfig()));
            var root = new LessonNodeDataDrawer().CreatePropertyGUI(nodeProp);

            var nodeTypeField = root.Q<EnumField>("node-type-field");

            Assert.IsNotNull(nodeTypeField);
            Assert.AreEqual(
                nodeProp.FindPropertyRelative("_nodeType").propertyPath,
                nodeTypeField.bindingPath);
        }

        [Test]
        public void CreatePropertyGUI_MismatchedNode_RendersWarningAndBothRepairButtonsWithoutMutating()
        {
            var populatedDialogue = new DialogueNodeConfig("seq_ui", "Text ui");
            var nodeProp = SetupNodeProperty(new LessonNodeData("guid-ui-2", NodeType.Quest, populatedDialogue));
            var drawer = new LessonNodeDataDrawer();

            var root = drawer.CreatePropertyGUI(nodeProp);

            Assert.IsNotNull(root);
            var warning = root.Q<HelpBox>("mismatch-warning");
            Assert.IsNotNull(warning);
            StringAssert.Contains("Quest", warning.text);
            StringAssert.Contains("DialogueNodeConfig", warning.text);

            var useConfigBtn = root.Q<Button>("use-config-type-button");
            var useNodeBtn = root.Q<Button>("use-node-type-button");
            Assert.IsNotNull(useConfigBtn);
            Assert.IsNotNull(useNodeBtn);

            // Discriminator and config must NOT have mutated automatically
            Assert.AreEqual((int)NodeType.Quest, nodeProp.FindPropertyRelative("_nodeType").enumValueIndex);
            Assert.AreSame(populatedDialogue, nodeProp.FindPropertyRelative("_config").managedReferenceValue);
        }

        [Test]
        public void CreatePropertyGUI_ClickUseConfigTypeButton_RepairsAndRefreshesUI()
        {
            var populatedDialogue = new DialogueNodeConfig("seq_btn", "Button test text");
            var nodeProp = SetupNodeProperty(new LessonNodeData("guid-ui-3", NodeType.Quest, populatedDialogue));
            var drawer = new LessonNodeDataDrawer();

            var root = AttachAndBind(drawer.CreatePropertyGUI(nodeProp));
            var useConfigBtn = root.Q<Button>("use-config-type-button");
            Assert.IsNotNull(useConfigBtn);

            Submit(useConfigBtn);

            Assert.IsNull(root.Q<HelpBox>("mismatch-warning"));
            Assert.IsNull(root.Q<VisualElement>("repair-buttons"));
            Assert.AreEqual((int)NodeType.Dialogue, nodeProp.FindPropertyRelative("_nodeType").enumValueIndex);
            var config = nodeProp.FindPropertyRelative("_config").managedReferenceValue as DialogueNodeConfig;
            Assert.IsNotNull(config);
            Assert.AreEqual("Button test text", config.Text);
        }

        [Test]
        public void CreatePropertyGUI_ClickUseNodeTypeButton_RepairsAndRefreshesUI()
        {
            var nodeProp = SetupNodeProperty(new LessonNodeData("guid-ui-4", NodeType.Dialogue, new QuestNodeConfig()));
            var drawer = new LessonNodeDataDrawer();

            var root = AttachAndBind(drawer.CreatePropertyGUI(nodeProp));
            var useNodeBtn = root.Q<Button>("use-node-type-button");
            Assert.IsNotNull(useNodeBtn);

            Submit(useNodeBtn);

            Assert.IsNull(root.Q<HelpBox>("mismatch-warning"));
            Assert.IsNull(root.Q<VisualElement>("repair-buttons"));
            Assert.AreEqual((int)NodeType.Dialogue, nodeProp.FindPropertyRelative("_nodeType").enumValueIndex);
            Assert.IsInstanceOf<DialogueNodeConfig>(nodeProp.FindPropertyRelative("_config").managedReferenceValue);
        }

        [Test]
        public void CreatePropertyGUI_ChangeNodeTypeDropdown_ChangesTypeAndConfig()
        {
            var nodeProp = SetupNodeProperty(new LessonNodeData("guid-ui-dd", NodeType.Quest, new QuestNodeConfig()));
            var drawer = new LessonNodeDataDrawer();

            var root = AttachAndBind(drawer.CreatePropertyGUI(nodeProp));
            var nodeTypeField = root.Q<EnumField>("node-type-field");
            Assert.IsNotNull(nodeTypeField);

            nodeTypeField.value = NodeType.Wait;

            Assert.AreEqual((int)NodeType.Wait, nodeProp.FindPropertyRelative("_nodeType").enumValueIndex);
            var config = nodeProp.FindPropertyRelative("_config").managedReferenceValue;
            Assert.IsNotNull(config);
            Assert.IsInstanceOf<WaitNodeConfig>(config);
        }

        [TestCase(NodeType.Timeline)]
        [TestCase(NodeType.Parallel)]
        [TestCase(NodeType.Gate)]
        public void CreatePropertyGUI_Phase2Node_RendersUnsupportedWarningWithoutMutating(NodeType phase2Type)
        {
            var nodeProp = SetupNodeProperty(new LessonNodeData("guid-ui-p2", phase2Type, null));
            var drawer = new LessonNodeDataDrawer();

            var root = drawer.CreatePropertyGUI(nodeProp);

            Assert.IsNotNull(root);
            var phase2Warning = root.Q<HelpBox>("phase2-warning");
            Assert.IsNotNull(phase2Warning);
            StringAssert.Contains(phase2Type.ToString(), phase2Warning.text);
            Assert.IsNull(root.Q<VisualElement>("repair-buttons"));
            Assert.IsNull(nodeProp.FindPropertyRelative("_config").managedReferenceValue);
        }

        [Test]
        public void CreatePropertyGUI_NullConfigPhase1_InitializesDefaultConfigImmediately()
        {
            var nodeProp = SetupNodeProperty(new LessonNodeData("guid-ui-null", NodeType.Wait, null));
            var drawer = new LessonNodeDataDrawer();

            var root = drawer.CreatePropertyGUI(nodeProp);

            Assert.IsNotNull(root);
            Assert.IsNull(root.Q<HelpBox>("mismatch-warning"));
            Assert.IsNotNull(root.Q<PropertyField>("config-field"));

            var config = nodeProp.FindPropertyRelative("_config").managedReferenceValue;
            Assert.IsNotNull(config);
            Assert.IsInstanceOf<WaitNodeConfig>(config);
        }

        [Test]
        public void CreatePropertyGUI_MissingManagedReference_DoesNotOverwritePayloadAndShowsWarning()
        {
            _tempAssetPath =
                "Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/__TempMissingNodeConfig.asset";
            AssetDatabase.DeleteAsset(_tempAssetPath);

            _graph.Editor_SetNodes(new List<LessonNodeData>
            {
                new LessonNodeData(
                    "missing-config-node",
                    NodeType.Quest,
                    new QuestNodeConfig(new List<string> { "preserve-me" }, 17f, "Preserve prompt")),
            });
            AssetDatabase.CreateAsset(_graph, _tempAssetPath);
            AssetDatabase.SaveAssets();

            var absolutePath = Path.GetFullPath(_tempAssetPath);
            var yaml = File.ReadAllText(absolutePath);
            StringAssert.Contains("class: QuestNodeConfig", yaml);
            File.WriteAllText(
                absolutePath,
                yaml.Replace("class: QuestNodeConfig", "class: MissingQuestNodeConfig"));
            AssetDatabase.ImportAsset(_tempAssetPath, ImportAssetOptions.ForceUpdate);

            _graph = AssetDatabase.LoadAssetAtPath<LessonGraph>(_tempAssetPath);
            var missingTypes = SerializationUtility.GetManagedReferencesWithMissingTypes(_graph);
            Assert.AreEqual(1, missingTypes.Length, "Fixture must contain exactly one missing reference.");

            _serializedObject = new SerializedObject(_graph);
            var nodeProp = _serializedObject.FindProperty("_nodes").GetArrayElementAtIndex(0);
            var configProp = nodeProp.FindPropertyRelative("_config");
            Assert.AreEqual(-2, configProp.managedReferenceId,
                "Unity exposes an unknown RID on a property whose managed type is missing.");

            var root = new LessonNodeDataDrawer().CreatePropertyGUI(nodeProp);

            Assert.IsNotNull(root.Q<HelpBox>("missing-reference-warning"));
            Assert.IsNull(configProp.managedReferenceValue);
            Assert.AreEqual(
                missingTypes.Single().referenceId,
                SerializationUtility.GetManagedReferencesWithMissingTypes(_graph).Single().referenceId,
                "Opening the drawer must preserve the missing serialized payload for recovery.");
        }

        [Test]
        public void CreatePropertyGUI_MultipleObjects_ShowsWarningAndDoesNotMutateMixedConfigs()
        {
            var firstConfig = new QuestNodeConfig(
                new List<string> { "first-binding" }, 8f, "First prompt");
            _graph.Editor_SetNodes(new List<LessonNodeData>
            {
                new LessonNodeData("first-node", NodeType.Quest, firstConfig),
            });

            var secondGraph = ScriptableObject.CreateInstance<LessonGraph>();
            try
            {
                secondGraph.Editor_SetNodes(new List<LessonNodeData>
                {
                    new LessonNodeData("second-node", NodeType.Quest, null),
                });
                var multiObject = new SerializedObject(
                    new UnityEngine.Object[] { _graph, secondGraph });
                var nodeProp = multiObject.FindProperty("_nodes").GetArrayElementAtIndex(0);

                var root = new LessonNodeDataDrawer().CreatePropertyGUI(nodeProp);

                Assert.IsNotNull(root.Q<HelpBox>("multi-edit-warning"));
                Assert.IsNull(root.Q<EnumField>("node-type-field"));
                Assert.AreSame(firstConfig, _graph.Nodes[0].Config);
                Assert.IsNull(secondGraph.Nodes[0].Config);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(secondGraph);
            }
        }
    }
}
