using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class ProductionLessonSceneMigrationTests
    {
        private const string DefinitionTypeName =
            "VRAutism.Gameplay.LessonGraphV2.Runtime.LessonGraphSceneDefinitionV2";

        private static readonly string[] PreservedV2ComponentTypes =
        {
            "VRAutism.Gameplay.LessonGraphV2.Runtime.LessonGraphRunner",
            "VRAutism.Gameplay.LessonGraphV2.Questing.LessonGraphBindings",
            "VRAutism.Gameplay.LessonGraphV2.Runtime.Dialogue.LiveKitDialogueTransportV2",
            "VRAutism.Gameplay.LessonGraphV2.Remote.LiveKitLessonRemoteBridgeV2",
            "VRAutism.Gameplay.LessonGraphV2.Runtime.TimelinePlaybackController",
            "VRAutism.Gameplay.LessonGraphV2.Runtime.LessonGraphDebugLogger",
            "VRAutism.Gameplay.LessonGraphV2.Integration.LessonGraphLifecycleV2",
            "VRAutism.Gameplay.LessonGraphV2.Integration.LessonGraphSensorBridgeV2",
            "VRAutism.Gameplay.LessonGraphV2.Presentation.LessonGraphHintPresenterV2",
            "VRAutism.Gameplay.LessonGraphV2.Integration.NpcAudioRouteBindingV2",
        };

        [TestCase("Assets/Project/Scenes/Bathroom-V2.unity", "ad32e9c811bab714783073ba2f9d7666")]
        [TestCase("Assets/Project/Scenes/LearnToAsk-V2.unity", "c334214253427c841b9dabb45326276a")]
        public void Scene_HasOneExplicitDefinitionWithOriginalEnabledReferences(string scenePath, string graphGuid)
        {
            var absoluteScenePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", scenePath));
            var scene = EditorSceneManager.OpenPreviewScene(absoluteScenePath);
            try
            {
                var components = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                    .Where(component => component != null)
                    .ToArray();
                var definitionType = FindType(DefinitionTypeName);
                var definitions = components.Where(component => definitionType.IsInstanceOfType(component)).ToArray();

                Assert.That(definitions, Has.Length.EqualTo(1), "The scene must contain exactly one explicit definition.");
                var definition = definitions[0];
                Assert.That(definition.enabled, Is.True);
                Assert.That(definition.gameObject.activeInHierarchy, Is.True);

                var graph = GetFieldValue(definition, "_lessonGraph") as UnityEngine.Object;
                Assert.That(graph, Is.Not.Null);
                var graphAssetPath = AssetDatabase.GetAssetPath(graph);
                Assert.That(AssetDatabase.AssetPathToGUID(graphAssetPath), Is.EqualTo(graphGuid));

                var runner = AssertSceneComponent(definition, "_runner");
                var bindings = AssertSceneComponent(definition, "_bindings");
                AssertSceneComponent(definition, "_dialogueTransport");
                AssertSceneComponent(definition, "_remoteBridge");
                var timelineController = AssertSceneComponent(definition, "_timelinePlaybackController");
                Assert.That(GetFieldValue(definition, "_variableStore"), Is.Null);

                var director = GetFieldValue(timelineController, "_director") as PlayableDirector;
                Assert.That(director, Is.Not.Null, "The node-owned Timeline director must remain assigned.");
                Assert.That(director.playOnAwake, Is.False,
                    "The lesson-owned director must wait for its graph node before playing.");

                Assert.That(runner.enabled, Is.True);
                Assert.That(bindings.enabled, Is.True);
                foreach (var typeName in PreservedV2ComponentTypes)
                {
                    var type = FindType(typeName);
                    var matches = components.Where(type.IsInstanceOfType).ToArray();
                    Assert.That(matches, Has.Length.EqualTo(1), $"Expected one preserved component of type {typeName}.");
                    Assert.That(matches[0].enabled, Is.True, $"{typeName} must retain its enabled state.");
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void SharedRuntimeCode_HasNoLessonOrSceneNameSwitches()
        {
            var runtimeDirectory = Path.Combine(Application.dataPath, "Project", "Scripts", "Gameplay",
                "LessonGraphV2", "Runtime");
            foreach (var sourcePath in Directory.GetFiles(runtimeDirectory, "*.cs", SearchOption.AllDirectories))
            {
                var source = File.ReadAllText(sourcePath);
                StringAssert.DoesNotContain("Bathroom-V2", source, sourcePath);
                StringAssert.DoesNotContain("LearnToAsk-V2", source, sourcePath);
            }
        }

        private static Behaviour AssertSceneComponent(Component definition, string fieldName)
        {
            var behaviour = GetFieldValue(definition, fieldName) as Behaviour;
            Assert.That(behaviour, Is.Not.Null, $"{fieldName} must resolve to a scene behaviour.");
            Assert.That(behaviour.gameObject.scene, Is.EqualTo(definition.gameObject.scene),
                $"{fieldName} must resolve within the same scene.");
            Assert.That(behaviour.enabled, Is.True, $"{fieldName} must retain its enabled state.");
            return behaviour;
        }

        private static object GetFieldValue(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected serialized field {fieldName} on {instance.GetType().FullName}.");
            return field.GetValue(instance);
        }

        private static Type FindType(string fullName)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(candidate => candidate != null);
            Assert.That(type, Is.Not.Null, $"Could not find required scene component type {fullName}.");
            return type;
        }
    }
}
