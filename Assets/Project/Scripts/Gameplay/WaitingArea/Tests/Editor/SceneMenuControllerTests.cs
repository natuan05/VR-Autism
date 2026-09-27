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
