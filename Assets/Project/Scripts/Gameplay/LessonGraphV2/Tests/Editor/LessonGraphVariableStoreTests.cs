using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Questing;
using VRAutism.Gameplay.LessonGraphV2.Runtime;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class LessonGraphVariableStoreTests
    {
        [Test]
        public void TypedSettersExposeValuesAndRemoveByOrdinalName()
        {
            var gameObject = new GameObject("LessonGraphVariableStoreTests");
            try
            {
                var store = gameObject.AddComponent<LessonGraphVariableStore>();
                store.SetBoolean("ready", true);
                store.SetInteger("attempts", 3);
                store.SetFloat("confidence", 0.75f);
                store.SetString("label", "finished");

                AssertValue(store, "ready", VariableValueType.Boolean, true);
                AssertValue(store, "attempts", VariableValueType.Integer, 3);
                AssertValue(store, "confidence", VariableValueType.Float, 0.75f);
                AssertValue(store, "label", VariableValueType.String, "finished");
                Assert.IsFalse(store.TryGetValue("Ready", out _), "Variable names use ordinal, case-sensitive lookup.");
                Assert.IsTrue(store.Remove("ready"));
                Assert.IsFalse(store.TryGetValue("ready", out _));
            }
            finally { UnityEngine.Object.DestroyImmediate(gameObject); }
        }

        [Test]
        public void InstallerSuppliesSceneVariableStoreToRunner()
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            var gameObject = new GameObject("LessonGraphVariableInstallerTests");
            gameObject.SetActive(false);
            graph.Editor_SetSchemaVersion(2);
            graph.Editor_SetEntryNodeId("start");
            graph.Editor_SetNodes(new System.Collections.Generic.List<LessonNodeData>
            {
                new LessonNodeData("start", NodeType.Wait, new WaitNodeConfig(0.01f)),
                new LessonNodeData("selected", NodeType.Wait, new WaitNodeConfig(0.01f)),
            });
            graph.Editor_SetEdges(new System.Collections.Generic.List<LessonEdgeData>
            {
                new LessonEdgeData("start", "selected", new VariableCondition("ready", VariableValueType.Boolean,
                    VariableComparisonOperator.Equal, true)),
            });
            try
            {
                var runner = gameObject.AddComponent<LessonGraphRunner>();
                gameObject.AddComponent<LessonGraphBindings>();
                var store = gameObject.AddComponent<LessonGraphVariableStore>();
                SetSerializedInitialBoolean(store, "ready", true);
                var installer = gameObject.AddComponent<LessonGraphRunnerInstaller>();
                typeof(LessonGraphRunnerInstaller).GetField("_lessonGraph", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(installer, graph);

                installer.Configure();
                var source = typeof(LessonGraphRunner).GetField("_variableSource", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(runner) as ILessonVariableSource;
                Assert.AreSame(store, source, "Installer must supply its scene store to the runner.");
                Assert.IsTrue(source.TryGetValue("ready", out var ready));
                Assert.IsTrue(ready.Boolean);
                Assert.IsTrue(LessonConditionEvaluator.Evaluate(graph.Edges[0].Condition, NodeStatus.Success, source));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        private static void AssertValue(LessonGraphVariableStore store, string name, VariableValueType type, object expected)
        {
            Assert.IsTrue(store.TryGetValue(name, out var value));
            Assert.AreEqual(type, value.Type);
            switch (type)
            {
                case VariableValueType.Boolean: Assert.AreEqual(expected, value.Boolean); break;
                case VariableValueType.Integer: Assert.AreEqual(expected, value.Integer); break;
                case VariableValueType.Float: Assert.AreEqual(expected, value.Float); break;
                case VariableValueType.String: Assert.AreEqual(expected, value.String); break;
            }
        }

        private static void SetSerializedInitialBoolean(LessonGraphVariableStore store, string name, bool value)
        {
            var entryType = typeof(LessonGraphVariableStore).GetNestedType("InitialValue", BindingFlags.NonPublic);
            var entry = Activator.CreateInstance(entryType, nonPublic: true);
            entryType.GetField("Name").SetValue(entry, name);
            entryType.GetField("Type").SetValue(entry, VariableValueType.Boolean);
            entryType.GetField("Boolean").SetValue(entry, value);
            var listType = typeof(List<>).MakeGenericType(entryType);
            var entries = (System.Collections.IList)Activator.CreateInstance(listType);
            entries.Add(entry);
            typeof(LessonGraphVariableStore).GetField("_initialValues", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(store, entries);
        }

    }
}
