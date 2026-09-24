using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
using VRAutism.Gameplay.LessonGraphV2.Editor;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    [TestFixture]
    public sealed class LessonEdgeDataDrawerTests
    {
        private LessonGraph _graph;
        private SerializedObject _serializedObject;
        private EditorWindow _window;
        private string _tempAssetPath;

        [SetUp]
        public void SetUp()
        {
            _tempAssetPath = null;
            _graph = ScriptableObject.CreateInstance<LessonGraph>();
            _graph.Editor_SetNodes(new List<LessonNodeData>
            {
                new LessonNodeData("node-a", NodeType.Quest, null),
                new LessonNodeData("node-b", NodeType.Dialogue, null),
            });
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

        [Test]
        public void CreatePropertyGUI_NullConditionDefaultsToSuccessAndExposesCanonicalOptions()
        {
            var edgeProperty = SetupEdgeProperty(
                new LessonEdgeData("node-a", "node-b", null));
            var conditionProperty = edgeProperty.FindPropertyRelative("_condition");
            Assert.AreEqual(-2, conditionProperty.managedReferenceId,
                "Unity represents an ordinary null SerializeReference with RefIdNull (-2).");

            var root = CreateEdgeDrawer().CreatePropertyGUI(edgeProperty);

            var condition = conditionProperty.managedReferenceValue;
            Assert.IsInstanceOf<StatusCondition>(condition);
            Assert.AreEqual("success", ((StatusCondition)condition).RequiredStatus);

            var conditionField = root.Q<PopupField<string>>("condition-type-field");
            Assert.IsNotNull(conditionField);
            CollectionAssert.AreEqual(
                new[] { "StatusCondition", "AlwaysCondition" },
                conditionField.choices);

            var statusField = root.Q<PopupField<string>>("required-status-field");
            Assert.IsNotNull(statusField);
            CollectionAssert.AreEqual(
                new[] { "success", "skipped", "timeout", "failed" },
                statusField.choices);
        }

        [Test]
        public void CreatePropertyGUI_FromAndToPopupsKeepAndLabelStaleIds()
        {
            var edgeProperty = SetupEdgeProperty(
                new LessonEdgeData("removed-node", "node-b", new AlwaysCondition()));

            var root = AttachAndBind(CreateEdgeDrawer().CreatePropertyGUI(edgeProperty));

            var fromField = root.Q<PopupField<string>>("from-node-field");
            var toField = root.Q<PopupField<string>>("to-node-field");
            Assert.IsNotNull(fromField);
            Assert.IsNotNull(toField);
            Assert.AreEqual("removed-node", fromField.value);
            Assert.AreEqual("node-b", toField.value);
            Assert.That(fromField.choices, Does.Contain("node-a"));
            Assert.That(fromField.choices, Does.Contain("removed-node"));
            Assert.AreEqual("(missing) removed-node", fromField.formatSelectedValueCallback(fromField.value));

            fromField.value = "node-a";

            Assert.AreEqual("node-a", edgeProperty.FindPropertyRelative("_fromNodeId").stringValue);
        }

        [Test]
        public void CreatePropertyGUI_ConditionTypeChangesAreUndoable()
        {
            var edgeProperty = SetupEdgeProperty(
                new LessonEdgeData("node-a", "node-b", new StatusCondition("timeout")));
            var root = AttachAndBind(CreateEdgeDrawer().CreatePropertyGUI(edgeProperty));

            var conditionField = root.Q<PopupField<string>>("condition-type-field");
            Assert.IsNotNull(conditionField);
            conditionField.value = "AlwaysCondition";

            Assert.IsInstanceOf<AlwaysCondition>(edgeProperty.FindPropertyRelative("_condition").managedReferenceValue);

            Undo.PerformUndo();
            _serializedObject.Update();
            Assert.IsInstanceOf<StatusCondition>(edgeProperty.FindPropertyRelative("_condition").managedReferenceValue);
            Assert.AreEqual("timeout", ((StatusCondition)edgeProperty.FindPropertyRelative("_condition").managedReferenceValue).RequiredStatus);

            Undo.PerformRedo();
            _serializedObject.Update();
            Assert.IsInstanceOf<AlwaysCondition>(edgeProperty.FindPropertyRelative("_condition").managedReferenceValue);
        }

        [Test]
        public void CreatePropertyGUI_StatusChangesAreUndoable()
        {
            var edgeProperty = SetupEdgeProperty(
                new LessonEdgeData("node-a", "node-b", new StatusCondition("success")));
            var root = AttachAndBind(CreateEdgeDrawer().CreatePropertyGUI(edgeProperty));

            root.Q<PopupField<string>>("required-status-field").value = "failed";

            Assert.AreEqual("failed", ((StatusCondition)edgeProperty.FindPropertyRelative("_condition").managedReferenceValue).RequiredStatus);

            Undo.PerformUndo();
            _serializedObject.Update();
            Assert.AreEqual("success", ((StatusCondition)edgeProperty.FindPropertyRelative("_condition").managedReferenceValue).RequiredStatus);

            Undo.PerformRedo();
            _serializedObject.Update();
            Assert.AreEqual("failed", ((StatusCondition)edgeProperty.FindPropertyRelative("_condition").managedReferenceValue).RequiredStatus);
        }

        [Test]
        public void CreatePropertyGUI_MissingConditionPreservesPayloadUntilExplicitReplaceAndReload()
        {
            _tempAssetPath =
                $"Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/__TempMissingEdgeCondition_{Guid.NewGuid():N}.asset";

            _graph.Editor_SetEdges(new List<LessonEdgeData>
            {
                new LessonEdgeData("node-a", "node-b", new StatusCondition(StatusCondition.Failed)),
            });
            AssetDatabase.CreateAsset(_graph, _tempAssetPath);
            AssetDatabase.SaveAssets();

            var absolutePath = Path.GetFullPath(_tempAssetPath);
            var yaml = File.ReadAllText(absolutePath);
            StringAssert.Contains("class: StatusCondition", yaml);
            StringAssert.Contains("_requiredStatus: failed", yaml);
            var missingTypeYaml = yaml.Replace("class: StatusCondition", "class: MissingStatusCondition");
            Assert.AreNotEqual(yaml, missingTypeYaml, "The fixture must rename its managed-reference type.");
            File.WriteAllText(absolutePath, missingTypeYaml);
            AssetDatabase.ImportAsset(_tempAssetPath, ImportAssetOptions.ForceUpdate);

            _graph = AssetDatabase.LoadAssetAtPath<LessonGraph>(_tempAssetPath);
            var missingTypes = SerializationUtility.GetManagedReferencesWithMissingTypes(_graph);
            Assert.AreEqual(1, missingTypes.Length, "Fixture must contain exactly one missing edge condition.");
            var missingReferenceId = missingTypes.Single().referenceId;
            StringAssert.Contains($"rid: {missingReferenceId}", File.ReadAllText(absolutePath),
                "The fixture's edge condition must have a persisted RID for exact missing-reference matching.");

            _serializedObject = new SerializedObject(_graph);
            var edgeProperty = _serializedObject.FindProperty("_edges").GetArrayElementAtIndex(0);
            var conditionProperty = edgeProperty.FindPropertyRelative("_condition");
            Assert.AreEqual(-2, conditionProperty.managedReferenceId);

            var root = AttachAndBind(CreateEdgeDrawer().CreatePropertyGUI(edgeProperty));
            Assert.IsNotNull(root.Q<HelpBox>("missing-condition-warning"));
            Assert.IsNull(conditionProperty.managedReferenceValue);
            Assert.AreEqual(missingReferenceId,
                SerializationUtility.GetManagedReferencesWithMissingTypes(_graph).Single().referenceId,
                "Opening the edge drawer must preserve the unknown RID and its recoverable YAML payload.");
            StringAssert.Contains("_requiredStatus: failed", File.ReadAllText(absolutePath));

            var replaceButton = root.Q<Button>("replace-condition-button");
            Assert.IsNotNull(replaceButton);
            Submit(replaceButton);

            Assert.IsNull(root.Q<HelpBox>("missing-condition-warning"));
            var replacement = conditionProperty.managedReferenceValue as StatusCondition;
            Assert.IsNotNull(replacement);
            Assert.AreEqual(StatusCondition.Success, replacement.RequiredStatus);

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(_tempAssetPath, ImportAssetOptions.ForceUpdate);
            _graph = AssetDatabase.LoadAssetAtPath<LessonGraph>(_tempAssetPath);
            var reloadedCondition = _graph.Edges[0].Condition as StatusCondition;
            Assert.IsNotNull(reloadedCondition);
            Assert.AreEqual(StatusCondition.Success, reloadedCondition.RequiredStatus);
            Assert.AreEqual(0, SerializationUtility.GetManagedReferencesWithMissingTypes(_graph).Length);
        }

        [Test]
        public void TryReadConditionReferenceId_UnrecognizedEdgesListFailsClosed()
        {
            var parser = typeof(LessonEdgeDataDrawer).GetMethod(
                "TryReadConditionReferenceId",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(parser);

            var malformedSections = new[]
            {
                new[] { "_edges:", "- malformed" },
                new[] { "_edges:", "- _fromNodeId: node-a", "malformed:" },
            };
            for (var index = 0; index < malformedSections.Length; index++)
            {
                object[] arguments = { malformedSections[index], index, 0L, false };
                var resolved = (bool)parser.Invoke(null, arguments);

                Assert.IsFalse(resolved,
                    "Unrecognized persisted list content cannot prove that the requested edge is new.");
                Assert.IsFalse((bool)arguments[3]);
            }
        }

        [Test]
        public void LessonGraphAsset_SaveAndReload_PreservesNodeConfigAndEdgeCondition()
        {
            _tempAssetPath =
                $"Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/__TempLessonGraphRoundTrip_{Guid.NewGuid():N}.asset";

            _graph.Editor_SetNodes(new List<LessonNodeData>
            {
                new LessonNodeData(
                    "node-a",
                    NodeType.Quest,
                    new QuestNodeConfig(new List<string> { "binding-round-trip" }, 23f, "Saved prompt")),
                new LessonNodeData("node-b", NodeType.Wait, new WaitNodeConfig(4f)),
            });
            _graph.Editor_SetEntryNodeId("node-a");
            _graph.Editor_SetEdges(new List<LessonEdgeData>
            {
                new LessonEdgeData("node-a", "node-b", new StatusCondition(StatusCondition.Failed), 2),
            });

            AssetDatabase.CreateAsset(_graph, _tempAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(_tempAssetPath, ImportAssetOptions.ForceUpdate);

            _graph = AssetDatabase.LoadAssetAtPath<LessonGraph>(_tempAssetPath);
            Assert.IsNotNull(_graph);
            var questConfig = _graph.Nodes[0].Config as QuestNodeConfig;
            Assert.IsNotNull(questConfig);
            CollectionAssert.AreEqual(new[] { "binding-round-trip" }, questConfig.CompletionBindingIds);
            Assert.AreEqual(23f, questConfig.TimeoutSeconds);
            Assert.AreEqual("Saved prompt", questConfig.VoicePrompt);

            var edge = _graph.Edges[0];
            Assert.AreEqual("node-a", edge.FromNodeId);
            Assert.AreEqual("node-b", edge.ToNodeId);
            Assert.AreEqual(2, edge.Priority);
            var condition = edge.Condition as StatusCondition;
            Assert.IsNotNull(condition);
            Assert.AreEqual(StatusCondition.Failed, condition.RequiredStatus);
        }

        private SerializedProperty SetupEdgeProperty(LessonEdgeData edge)
        {
            _graph.Editor_SetEdges(new List<LessonEdgeData> { edge });
            _serializedObject = new SerializedObject(_graph);
            return _serializedObject.FindProperty("_edges").GetArrayElementAtIndex(0);
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
            root.Bind(_serializedObject);
            return root;
        }

        private static void Submit(Button button)
        {
            button.Focus();
            using (var submitEvent = NavigationSubmitEvent.GetPooled())
            {
                button.SendEvent(submitEvent);
            }
        }

        private static PropertyDrawer CreateEdgeDrawer()
        {
            const string DrawerTypeName = "VRAutism.Gameplay.LessonGraphV2.Editor.LessonEdgeDataDrawer";
            var drawerType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(DrawerTypeName, false))
                .FirstOrDefault(type => type != null);
            Assert.IsNotNull(drawerType, "The LessonEdgeData property drawer must be registered.");
            return (PropertyDrawer)Activator.CreateInstance(drawerType);
        }
    }
}
