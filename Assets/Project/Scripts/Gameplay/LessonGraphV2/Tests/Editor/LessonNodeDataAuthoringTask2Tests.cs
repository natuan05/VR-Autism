using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Editor;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    [TestFixture]
    public sealed class LessonNodeDataAuthoringTask2Tests
    {
        private LessonGraph _graph;
        private SerializedObject _serializedObject;
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

            if (_graph != null)
            {
                Undo.ClearUndo(_graph);
                Object.DestroyImmediate(_graph);
            }
        }

        [Test]
        public void CreatePropertyGUI_BlankNodeId_RequiresExplicitGenerateAction()
        {
            _graph.Editor_SetNodes(new List<LessonNodeData>
            {
                new LessonNodeData(string.Empty, NodeType.Quest, new QuestNodeConfig()),
            });
            _serializedObject = new SerializedObject(_graph);
            var nodeProperty = _serializedObject.FindProperty("_nodes").GetArrayElementAtIndex(0);
            var idProperty = nodeProperty.FindPropertyRelative("_id");

            var root = new LessonNodeDataDrawer().CreatePropertyGUI(nodeProperty);

            Assert.AreEqual(string.Empty, idProperty.stringValue,
                "Opening a node drawer must not silently replace a blank ID.");
            var generateButton = root.Q<Button>("generate-node-id-button");
            Assert.IsNotNull(generateButton, "A blank node ID needs an explicit Generate action.");

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
            root.Bind(_serializedObject);
            generateButton.Focus();
            using (var submitEvent = NavigationSubmitEvent.GetPooled())
            {
                generateButton.SendEvent(submitEvent);
            }

            _serializedObject.Update();
            Assert.IsNotEmpty(idProperty.stringValue,
                "Only the explicit Generate action should assign a new node ID.");
        }

        [Test]
        public void CreatePropertyGUI_ExistingNodeId_IsPreservedWithoutGenerateAction()
        {
            _graph.Editor_SetNodes(new List<LessonNodeData>
            {
                new LessonNodeData("stable-existing-id", NodeType.Quest, new QuestNodeConfig()),
            });
            _serializedObject = new SerializedObject(_graph);
            var nodeProperty = _serializedObject.FindProperty("_nodes").GetArrayElementAtIndex(0);

            var root = new LessonNodeDataDrawer().CreatePropertyGUI(nodeProperty);

            Assert.AreEqual("stable-existing-id", nodeProperty.FindPropertyRelative("_id").stringValue);
            Assert.IsNull(root.Q<Button>("generate-node-id-button"),
                "Existing node identities must remain unchanged unless an explicit workflow requests it.");
        }
    }
}
