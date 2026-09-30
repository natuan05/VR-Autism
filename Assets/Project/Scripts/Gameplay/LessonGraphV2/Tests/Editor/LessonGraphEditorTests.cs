using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    [TestFixture]
    public sealed class LessonGraphEditorTests
    {
        private LessonGraph _graph;
        private UnityEditor.Editor _editor;
        private EditorWindow _window;

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
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                {
                    LogAssert.Expect(LogType.Error, "No graphic device is available to initialize the view.");
                    LogAssert.Expect(LogType.Error, "No graphic device is available to show the window.");
                    LogAssert.Expect(LogType.Error, "No graphic device is available to initialize the view.");
                    LogAssert.Expect(LogType.Error, "No graphic device is available to show the window.");
                }

                _window.Close();
            }

            if (_editor != null)
            {
                Object.DestroyImmediate(_editor);
            }

            if (_graph != null)
            {
                Undo.ClearUndo(_graph);
                Object.DestroyImmediate(_graph);
            }
        }

        [Test]
        public void CreateInspectorGUI_EntryNodeDropdownKeepsAndShowsStaleId()
        {
            _graph.Editor_SetNodes(new List<LessonNodeData>
            {
                new LessonNodeData("start", NodeType.Wait, new WaitNodeConfig(1f)),
                new LessonNodeData("next", NodeType.Wait, new WaitNodeConfig(1f)),
            });
            _graph.Editor_SetEdges(new List<LessonEdgeData>());
            _graph.Editor_SetEntryNodeId("removed-start");

            var root = AttachAndBind(CreateInspectorRoot());

            var entryField = root.Q<PopupField<string>>("entry-node-field");
            Assert.IsNotNull(entryField);
            Assert.AreEqual("removed-start", entryField.value);
            Assert.That(entryField.choices, Does.Contain("start"));
            Assert.That(entryField.choices, Does.Contain("next"));
            Assert.That(entryField.choices, Does.Contain("removed-start"));
            Assert.AreEqual("(missing) removed-start", entryField.formatSelectedValueCallback(entryField.value));

            entryField.value = "next";

            Assert.AreEqual("next", _graph.EntryNodeId);
        }

        [Test]
        public void CreateInspectorGUI_ValidationPanelRendersCurrentValidatorErrors()
        {
            _graph.Editor_SetNodes(new List<LessonNodeData>
            {
                new LessonNodeData(string.Empty, NodeType.Quest, new QuestNodeConfig()),
            });
            _graph.Editor_SetEntryNodeId(string.Empty);

            var root = CreateInspectorRoot();

            var panel = root?.Q<VisualElement>("validation-panel");
            Assert.IsNotNull(panel);
            var errorText = string.Join("\n", panel.Query<HelpBox>().ToList().Select(helpBox => helpBox.text));
            StringAssert.Contains("MissingEntryNodeId", errorText);
            StringAssert.Contains("InvalidNodeId", errorText);
        }

        [UnityTest]
        public IEnumerator CreateInspectorGUI_CreatesAndEditsEdgeWithUndoAndRedo()
        {
            _graph.Editor_SetNodes(new List<LessonNodeData>
            {
                new LessonNodeData("start", NodeType.Wait, new WaitNodeConfig(1f)),
                new LessonNodeData("next", NodeType.Wait, new WaitNodeConfig(1f)),
            });

            var serializedObject = new SerializedObject(_graph);
            Undo.IncrementCurrentGroup();

            var edgesProperty = serializedObject.FindProperty("_edges");
            edgesProperty.arraySize = 1;
            edgesProperty.isExpanded = true;
            var edgeProperty = edgesProperty.GetArrayElementAtIndex(0);
            edgeProperty.isExpanded = true;
            edgeProperty.FindPropertyRelative("_fromNodeId").stringValue = "start";
            edgeProperty.FindPropertyRelative("_toNodeId").stringValue = "next";
            edgeProperty.FindPropertyRelative("_priority").intValue = 3;
            edgeProperty.FindPropertyRelative("_condition").managedReferenceValue =
                new StatusCondition(StatusCondition.Success);
            serializedObject.ApplyModifiedProperties();
            Undo.IncrementCurrentGroup();

            var root = AttachAndBind(CreateInspectorRoot());
            serializedObject = _editor.serializedObject;
            yield return null;
            yield return null;

            Assert.AreEqual(1, _graph.Edges.Count, "The serialized inspector array must create the edge.");
            Assert.AreEqual("start", _graph.Edges[0].FromNodeId);
            Assert.AreEqual("next", _graph.Edges[0].ToNodeId);
            Assert.AreEqual(3, _graph.Edges[0].Priority);

            var statusField = root.Q<PopupField<string>>("required-status-field");
            Assert.IsNotNull(statusField, "The graph inspector must render the edge condition drawer.");
            statusField.value = StatusCondition.Failed;
            Assert.AreEqual(StatusCondition.Failed,
                ((StatusCondition)_graph.Edges[0].Condition).RequiredStatus);

            Undo.PerformUndo();
            serializedObject.Update();
            yield return null;
            yield return null;

            Assert.AreEqual(StatusCondition.Success,
                ((StatusCondition)_graph.Edges[0].Condition).RequiredStatus,
                "Undo must restore the created edge's prior condition.");
            statusField = root.Q<PopupField<string>>("required-status-field");
            Assert.IsNotNull(statusField);
            Assert.AreEqual(StatusCondition.Success, statusField.value,
                "The visible edge condition field must refresh after Undo.");

            Undo.PerformUndo();
            serializedObject.Update();
            yield return null;
            yield return null;

            Assert.AreEqual(0, _graph.Edges.Count, "A second Undo must remove the newly created edge.");

            Undo.PerformRedo();
            serializedObject.Update();
            yield return null;
            yield return null;

            Assert.AreEqual(1, _graph.Edges.Count);
            Assert.AreEqual(StatusCondition.Success,
                ((StatusCondition)_graph.Edges[0].Condition).RequiredStatus);

            Undo.PerformRedo();
            serializedObject.Update();
            yield return null;
            yield return null;

            Assert.AreEqual(StatusCondition.Failed,
                ((StatusCondition)_graph.Edges[0].Condition).RequiredStatus);
            statusField = root.Q<PopupField<string>>("required-status-field");
            Assert.IsNotNull(statusField);
            Assert.AreEqual(StatusCondition.Failed, statusField.value,
                "The visible edge condition field must refresh after Redo.");
        }

        [UnityTest]
        public IEnumerator AddQuestNode_CreatesEmptyIndependentConfigWithUndoAndRedo()
        {
            _graph.Editor_SetNodes(new List<LessonNodeData>
            {
                new LessonNodeData("original", NodeType.Quest,
                    new QuestNodeConfig(new List<string> { "original-binding" }, 12f, "original prompt")),
            });
            var root = AttachAndBind(CreateInspectorRoot());
            yield return null;
            yield return null;

            var list = root.Q<ListView>("nodes-property");
            Assert.IsNotNull(list);
            Assert.IsNotNull(list.overridingAddButtonBehavior);
            Undo.IncrementCurrentGroup();
            list.overridingAddButtonBehavior(list, null);

            Assert.AreEqual(2, _graph.Nodes.Count);
            var added = _graph.Nodes[1];
            Assert.IsNotEmpty(added.Id);
            Assert.AreNotEqual("original", added.Id);
            Assert.AreEqual(NodeType.Quest, added.NodeType);
            var config = (QuestNodeConfig)added.Config;
            Assert.AreNotSame(_graph.Nodes[0].Config, config);
            Assert.IsEmpty(config.CompletionBindingIds);
            Assert.AreEqual(-1f, config.TimeoutSeconds);
            Assert.IsEmpty(config.VoicePrompt);
            var addedId = added.Id;

            Undo.PerformUndo();
            _editor.serializedObject.Update();
            Assert.AreEqual(1, _graph.Nodes.Count);
            Undo.PerformRedo();
            _editor.serializedObject.Update();
            Assert.AreEqual(2, _graph.Nodes.Count);
            Assert.AreEqual(addedId, _graph.Nodes[1].Id);
            Assert.IsEmpty(((QuestNodeConfig)_graph.Nodes[1].Config).CompletionBindingIds);
            CollectionAssert.AreEqual(new[] { "original-binding" },
                ((QuestNodeConfig)_graph.Nodes[0].Config).CompletionBindingIds);
        }

        [UnityTest]
        public IEnumerator AddEdge_CreatesFreshDefaultConditionWithUndoAndRedo()
        {
            _graph.Editor_SetEdges(new List<LessonEdgeData>
            {
                new LessonEdgeData("a", "b", new StatusCondition(StatusCondition.Failed), 7),
            });
            var root = AttachAndBind(CreateInspectorRoot());
            yield return null;
            yield return null;

            var list = root.Q<ListView>("edges-property");
            Assert.IsNotNull(list);
            Assert.IsNotNull(list.overridingAddButtonBehavior);
            Undo.IncrementCurrentGroup();
            list.overridingAddButtonBehavior(list, null);

            Assert.AreEqual(2, _graph.Edges.Count);
            Assert.IsEmpty(_graph.Edges[1].FromNodeId);
            Assert.IsEmpty(_graph.Edges[1].ToNodeId);
            Assert.AreEqual(0, _graph.Edges[1].Priority);
            Assert.AreNotSame(_graph.Edges[0].Condition, _graph.Edges[1].Condition);
            Assert.AreEqual(StatusCondition.Success, ((StatusCondition)_graph.Edges[1].Condition).RequiredStatus);

            Undo.PerformUndo();
            _editor.serializedObject.Update();
            Assert.AreEqual(1, _graph.Edges.Count);
            Undo.PerformRedo();
            _editor.serializedObject.Update();
            Assert.AreEqual(2, _graph.Edges.Count);
            Assert.AreNotSame(_graph.Edges[0].Condition, _graph.Edges[1].Condition);
            Assert.AreEqual(StatusCondition.Failed, ((StatusCondition)_graph.Edges[0].Condition).RequiredStatus);
            Assert.AreEqual(StatusCondition.Success, ((StatusCondition)_graph.Edges[1].Condition).RequiredStatus);
        }

        private VisualElement CreateInspectorRoot()
        {
            _editor = UnityEditor.Editor.CreateEditor(_graph);
            Assert.IsNotNull(_editor);
            var root = _editor.CreateInspectorGUI();
            Assert.IsNotNull(root,
                "LessonGraph assets need the custom inspector that contains the entry selector and validation panel.");
            return root;
        }

        private VisualElement AttachAndBind(VisualElement root)
        {
            _window = ScriptableObject.CreateInstance<EditorWindow>();
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                LogAssert.Expect(LogType.Error, "No graphic device is available to initialize the view.");
                LogAssert.Expect(LogType.Error, "No graphic device is available to show the window.");
                LogAssert.Expect(LogType.Error, "No graphic device is available to initialize the view.");
                LogAssert.Expect(LogType.Error, "No graphic device is available to show the window.");
            }

            _window.Show();
            _window.rootVisualElement.Add(root);
            root.Bind(_editor.serializedObject);
            return root;
        }
    }
}
