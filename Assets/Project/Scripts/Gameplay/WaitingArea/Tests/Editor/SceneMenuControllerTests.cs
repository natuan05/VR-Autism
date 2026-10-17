using System;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRAutism.Cloud.RTDB;
using VRAutism.Gameplay.WaitingArea;

namespace VRAutism.Gameplay.WaitingArea.Tests.Editor
{
    public sealed class SceneMenuControllerTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private GameObject _controllerObject;
        private GameObject _pairingObject;

        [TearDown]
        public void TearDown()
        {
            if (_controllerObject != null) UnityEngine.Object.DestroyImmediate(_controllerObject);
            if (_pairingObject != null) UnityEngine.Object.DestroyImmediate(_pairingObject);
            SceneMenuController.Instance = null;
            SetPairingManagerInstance(null);
        }

        [Test]
        public void Awake_DuplicateComponentOnSharedGameObject_DisablesDuplicateAndKeepsOwner()
        {
            _controllerObject = NewInactiveObject("Scene controller");
            var owner = _controllerObject.AddComponent<SceneMenuController>();
            var duplicate = _controllerObject.AddComponent<SceneMenuController>();

            Invoke(owner, "Awake");
            Invoke(duplicate, "Awake");

            Assert.AreSame(owner, SceneMenuController.Instance);
            Assert.IsFalse(duplicate.enabled, "The duplicate component must not receive Unity callbacks.");
            Assert.IsFalse(_controllerObject == null, "The shared GameObject must remain alive.");
        }

        [Test]
        public void Start_OnlyOwnerSubscribesOnce_AndUnsubscribesFromOriginalManager()
        {
            _pairingObject = NewInactiveObject("Pairing manager");
            var manager = _pairingObject.AddComponent<PairingManager>();
            SetPairingManagerInstance(manager);

            _controllerObject = NewInactiveObject("Scene controller");
            var owner = _controllerObject.AddComponent<SceneMenuController>();
            var duplicate = _controllerObject.AddComponent<SceneMenuController>();
            Invoke(owner, "Awake");
            Invoke(duplicate, "Awake");
            Invoke(owner, "Start");
            Invoke(owner, "Start");
            Invoke(duplicate, "Start");

            Assert.AreEqual(1, SessionCommandHandlerCount(manager));

            SetPairingManagerInstance(null);
            Invoke(owner, "OnDestroy");

            Assert.AreEqual(0, SessionCommandHandlerCount(manager));
            Assert.IsNull(SceneMenuController.Instance);

            var replacement = _controllerObject.AddComponent<SceneMenuController>();
            Invoke(replacement, "Awake");
            Invoke(owner, "OnDestroy");
            Assert.AreSame(replacement, SceneMenuController.Instance);
        }

        [Test]
        public void LoadRemoteLesson_InvalidCommand_ReturnsBeforeFirebaseOrSceneLoad()
        {
            _controllerObject = NewInactiveObject("Scene controller");
            var controller = _controllerObject.AddComponent<SceneMenuController>();
            Invoke(controller, "Awake");
            LogAssert.Expect(LogType.Warning, new Regex("lessonId.*sceneName"));

            Invoke(controller, "LoadRemoteLesson", "child", "Bathroom-V2", "", "session", "host", "token");

            Assert.IsFalse(GetField<bool>(controller, "_launchInProgress"));
        }

        [Test]
        public void LoadRemoteLesson_SecondCommandWhileLoading_ReturnsBeforeFirebaseOrSceneLoad()
        {
            _controllerObject = NewInactiveObject("Scene controller");
            var controller = _controllerObject.AddComponent<SceneMenuController>();
            Invoke(controller, "Awake");
            SetField(controller, "_launchInProgress", true);
            LogAssert.Expect(LogType.Warning, new Regex("launch.*progress|already.*load", RegexOptions.IgnoreCase));

            Invoke(controller, "LoadRemoteLesson", "child", "Bathroom-V2", "lesson", "session", "host", "token");

            Assert.IsTrue(GetField<bool>(controller, "_launchInProgress"));
        }

        [TestCase("WashingHand_2", "practical", 2, true, "Bathroom-V2", "Bathroom-V2", true, true, true)]
        [TestCase("Greet_1", "practical", null, false, "HelloFriend", "HelloFriend", true, true, false)]
        [TestCase("Greet_1", "practical", 2, true, "HelloFriend", "HelloFriend", true, true, true)]
        [TestCase("Farm_Quiz_1", "quiz", null, false, "Farm-Quiz", "Farm-Quiz", true, true, false)]
        [TestCase("Farm_1", "theoretical", null, false, "Farm", "Farm", true, true, false)]
        public void TryValidateLessonRuntime_AcceptsSupportedRuntimeAndSceneMatches(
            string documentId, string type, object schema, bool hasSchema, string storedScene, string requestedScene,
            bool enabled, bool expected, bool expectedV2)
        {
            var accepted = SceneMenuController.TryValidateLessonRuntime(
                documentId, type, schema, hasSchema, storedScene, requestedScene, enabled, out var isV2);

            Assert.AreEqual(expected, accepted);
            Assert.AreEqual(expectedV2, isV2);
        }

        [TestCase(false, null)]
        [TestCase(true, null)]
        [TestCase(true, "2")]
        [TestCase(true, true)]
        [TestCase(true, 1)]
        [TestCase(true, 3)]
        public void TryValidateLessonRuntime_RejectsUnsupportedPracticalSchema(bool hasSchema, object schema)
        {
            var accepted = SceneMenuController.TryValidateLessonRuntime(
                "WashingHand_2", "practical", schema, hasSchema, "Bathroom", "Bathroom", true, out var isV2);

            Assert.IsFalse(accepted);
            Assert.IsFalse(isV2);
        }

        [TestCase("Bathroom-V2", "Bathroom", true)]
        [TestCase("", "Bathroom", true)]
        [TestCase("Bathroom", "", true)]
        [TestCase("Bathroom", "Bathroom", false)]
        [TestCase("", "", true)]
        public void TryValidateLessonRuntime_RejectsMismatchedEmptyOrDisabledScene(string storedScene, string requestedScene, bool enabled)
        {
            var accepted = SceneMenuController.TryValidateLessonRuntime(
                "WashingHand_2", "practical", 2, true, storedScene, requestedScene, enabled, out var isV2);

            Assert.IsFalse(accepted);
            Assert.IsFalse(isV2);
        }

        [Test]
        public void TryValidateLessonRuntime_RejectsMissingDocumentIdInsteadOfFallingBackToRequestedScene()
        {
            var accepted = SceneMenuController.TryValidateLessonRuntime(
                "", "practical", 2, true, "Bathroom-V2", "Bathroom-V2", true, out var isV2);

            Assert.IsFalse(accepted);
            Assert.IsFalse(isV2);
        }

        private static GameObject NewInactiveObject(string name)
        {
            var value = new GameObject(name);
            value.SetActive(false);
            return value;
        }

        private static int SessionCommandHandlerCount(PairingManager manager)
        {
            var field = typeof(PairingManager).GetField("OnNewSessionCommand", PrivateInstance);
            var handlers = field.GetValue(manager) as Delegate;
            return handlers == null ? 0 : handlers.GetInvocationList().Length;
        }

        private static void SetPairingManagerInstance(PairingManager value)
        {
            typeof(PairingManager).GetField("<Instance>k__BackingField", PrivateStatic).SetValue(null, value);
        }

        private static T GetField<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name, PrivateInstance).GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
        }

        private static object Invoke(object target, string name, params object[] arguments)
        {
            return target.GetType().GetMethod(name, PrivateInstance).Invoke(target, arguments);
        }
    }
}
