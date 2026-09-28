using System.IO;
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
using VRAutism.Gameplay.LessonGraphV2.Editor;
using VRAutism.Gameplay.LessonGraphV2.Validation;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class AdvancedGraphAuthoringTests
    {
        private static EditorWindow AttachAndBind(VisualElement root, SerializedObject serializedObject)
        {
            var window = ScriptableObject.CreateInstance<EditorWindow>();
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                LogAssert.Expect(LogType.Error, "No graphic device is available to initialize the view.");
                LogAssert.Expect(LogType.Error, "No graphic device is available to show the window.");
                LogAssert.Expect(LogType.Error, "No graphic device is available to initialize the view.");
                LogAssert.Expect(LogType.Error, "No graphic device is available to show the window.");
            }

            window.Show();
            window.rootVisualElement.Add(root);
            root.Bind(serializedObject);
            return window;
        }

        private static void CloseWindow(EditorWindow window)
        {
            if (window == null) return;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                LogAssert.Expect(LogType.Error, "No graphic device is available to initialize the view.");
                LogAssert.Expect(LogType.Error, "No graphic device is available to show the window.");
                LogAssert.Expect(LogType.Error, "No graphic device is available to initialize the view.");
                LogAssert.Expect(LogType.Error, "No graphic device is available to show the window.");
            }

            window.Close();
        }

        [Test]
        public void AdvancedTypeCreatesTypedConfigAndPreservesNodeIdentityAndPosition()
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            try
            {
                graph.Editor_SetSchemaVersion(2);
                var node = new LessonNodeData("stable", NodeType.Wait, new WaitNodeConfig(3), new Vector2(12, 34));
                graph.Editor_SetNodes(new System.Collections.Generic.List<LessonNodeData> { node });
                var serialized = new SerializedObject(graph);
                var property = serialized.FindProperty("_nodes").GetArrayElementAtIndex(0);

                Assert.IsTrue(LessonNodeSyncHelper.ChangeNodeType(property, NodeType.Parallel));

                Assert.AreEqual("stable", node.Id);
                Assert.AreEqual(new Vector2(12, 34), node.Position);
                Assert.IsInstanceOf<ParallelNodeConfig>(node.Config);
                Undo.PerformUndo();
                serialized.Update();
                Assert.AreEqual(NodeType.Wait, node.NodeType);
                Assert.IsInstanceOf<WaitNodeConfig>(node.Config);
            }
            finally { Object.DestroyImmediate(graph); Undo.ClearAll(); }
        }

        [Test]
        public void SchemaTwoEdgeInspectorCanSelectTypedVariableCondition()
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            EditorWindow window = null;
            try
            {
                graph.Editor_SetSchemaVersion(2);
                graph.Editor_SetNodes(new System.Collections.Generic.List<LessonNodeData>
                {
                    new LessonNodeData("a", NodeType.Wait, new WaitNodeConfig(1)),
                    new LessonNodeData("b", NodeType.Wait, new WaitNodeConfig(1))
                });
                graph.Editor_SetEdges(new System.Collections.Generic.List<LessonEdgeData>
                {
                    new LessonEdgeData("a", "b", new AlwaysCondition())
                });
                var serialized = new SerializedObject(graph);
                var edge = serialized.FindProperty("_edges").GetArrayElementAtIndex(0);
                var root = new LessonEdgeDataDrawer().CreatePropertyGUI(edge);
                window = AttachAndBind(root, serialized);

                root.Q<PopupField<string>>("condition-type-field").value = nameof(VariableCondition);

                Assert.IsInstanceOf<VariableCondition>(edge.FindPropertyRelative("_condition").managedReferenceValue);
            }
            finally { CloseWindow(window); Object.DestroyImmediate(graph); Undo.ClearAll(); }
        }

        [Test]
        public void GraphInspectorShowsSchemaVersionWithoutEditableField()
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            var editor = UnityEditor.Editor.CreateEditor(graph);
            try
            {
                var root = editor.CreateInspectorGUI();
                Assert.AreEqual("Schema Version: 1", root.Q<Label>("schema-version-label").text);
                Assert.IsNull(root.Q<PropertyField>("schema-version-field"));
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void LoopDefaultCanBeCompletedWithTypedExitConditionInInspector()
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            EditorWindow window = null;
            try
            {
                graph.Editor_SetSchemaVersion(2);
                graph.Editor_SetEntryNodeId("loop");
                graph.Editor_SetNodes(new System.Collections.Generic.List<LessonNodeData>
                {
                    new LessonNodeData("loop", NodeType.Loop, new LoopNodeConfig()),
                    new LessonNodeData("body", NodeType.Wait, new WaitNodeConfig(1)),
                    new LessonNodeData("exit", NodeType.Wait, new WaitNodeConfig(1))
                });
                var serialized = new SerializedObject(graph);
                var loopNode = serialized.FindProperty("_nodes").GetArrayElementAtIndex(0);
                var loopConfig = loopNode.FindPropertyRelative("_config");
                loopConfig.FindPropertyRelative("_bodyChildNodeId").stringValue = "body";
                loopConfig.FindPropertyRelative("_exitNodeId").stringValue = "exit";
                serialized.ApplyModifiedProperties();

                var root = new LessonNodeDataDrawer().CreatePropertyGUI(loopNode);
                window = AttachAndBind(root, serialized);
                root.Q<PopupField<string>>("loop-exit-condition-type-field").value = nameof(AlwaysCondition);

                serialized.Update();
                Assert.IsInstanceOf<AlwaysCondition>(
                    serialized.FindProperty("_nodes").GetArrayElementAtIndex(0)
                        .FindPropertyRelative("_config").FindPropertyRelative("_exitCondition").managedReferenceValue);
                Assert.IsTrue(LessonGraphValidator.Validate(graph).IsValid,
                    LessonGraphValidator.Validate(graph).ToString());
            }
            finally { CloseWindow(window); Object.DestroyImmediate(graph); Undo.ClearAll(); }
        }

        [Test]
        public void CompositeDefaultCanReceiveTypedChildrenInInspector()
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            EditorWindow window = null;
            try
            {
                graph.Editor_SetSchemaVersion(2);
                graph.Editor_SetEntryNodeId("a");
                graph.Editor_SetNodes(new System.Collections.Generic.List<LessonNodeData>
                {
                    new LessonNodeData("a", NodeType.Wait, new WaitNodeConfig(1)),
                    new LessonNodeData("b", NodeType.Wait, new WaitNodeConfig(1))
                });
                graph.Editor_SetEdges(new System.Collections.Generic.List<LessonEdgeData>
                {
                    new LessonEdgeData("a", "b", new AlwaysCondition())
                });
                var serialized = new SerializedObject(graph);
                var edge = serialized.FindProperty("_edges").GetArrayElementAtIndex(0);
                var root = new LessonEdgeDataDrawer().CreatePropertyGUI(edge);
                window = AttachAndBind(root, serialized);
                root.Q<PopupField<string>>("condition-type-field").value = nameof(CompositeCondition);
                root.Q<PopupField<string>>("edge-condition-add-child-condition-field").value = nameof(AlwaysCondition);

                serialized.Update();
                var composite = serialized.FindProperty("_edges").GetArrayElementAtIndex(0)
                    .FindPropertyRelative("_condition").managedReferenceValue as CompositeCondition;
                Assert.IsNotNull(composite);
                Assert.AreEqual(1, composite.Conditions.Count);
                Assert.IsInstanceOf<AlwaysCondition>(composite.Conditions[0]);
                Assert.IsTrue(LessonGraphValidator.Validate(graph).IsValid,
                    LessonGraphValidator.Validate(graph).ToString());
            }
            finally { CloseWindow(window); Object.DestroyImmediate(graph); Undo.ClearAll(); }
        }

        [Test]
        public void NestedMissingConditionReferenceIsPreservedAndCannotBeReplaced()
        {
            var path = "Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/__TempNestedCondition_" +
                       System.Guid.NewGuid().ToString("N") + ".asset";
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            try
            {
                graph.Editor_SetEdges(new System.Collections.Generic.List<LessonEdgeData>
                {
                    new LessonEdgeData("a", "b", new CompositeCondition(CompositeConditionOperator.And,
                        new IEdgeCondition[] { new StatusCondition(StatusCondition.Failed) }))
                });
                AssetDatabase.CreateAsset(graph, path);
                AssetDatabase.SaveAssets();
                var absolutePath = Path.GetFullPath(path);
                var yaml = File.ReadAllText(absolutePath);
                var missingYaml = yaml.Replace("class: StatusCondition", "class: MissingNestedStatusCondition");
                Assert.AreNotEqual(yaml, missingYaml);
                File.WriteAllText(absolutePath, missingYaml);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

                graph = AssetDatabase.LoadAssetAtPath<LessonGraph>(path);
                var missing = SerializationUtility.GetManagedReferencesWithMissingTypes(graph);
                Assert.AreEqual(1, missing.Length);
                var serialized = new SerializedObject(graph);
                var condition = serialized.FindProperty("_edges").GetArrayElementAtIndex(0)
                    .FindPropertyRelative("_condition");
                var nested = condition.FindPropertyRelative("_conditions").GetArrayElementAtIndex(0);
                Assert.AreEqual(-2, nested.managedReferenceId);

                var controls = ConditionAuthoringControls.CreateReferenceControl("Child", nested, null,
                    namePrefix: "condition-child");

                Assert.IsNotNull(controls.Q<HelpBox>("condition-child-missing-reference-warning"));
                Assert.IsNull(nested.managedReferenceValue);
                Assert.AreEqual(missing[0].referenceId,
                    SerializationUtility.GetManagedReferencesWithMissingTypes(graph).Single().referenceId);
                StringAssert.Contains("_requiredStatus: failed", File.ReadAllText(absolutePath));
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
                if (graph != null && !AssetDatabase.Contains(graph)) Object.DestroyImmediate(graph);
                Undo.ClearAll();
            }
        }
    }
}
