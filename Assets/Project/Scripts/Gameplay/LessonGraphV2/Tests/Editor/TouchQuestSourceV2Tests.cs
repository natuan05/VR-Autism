using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Questing;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class TouchQuestSourceV2Tests
    {
        private readonly List<Object> _objects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in _objects) if (item != null) Object.DestroyImmediate(item);
            _objects.Clear();
        }

        [Test]
        public void AllowedTrigger_CompletesOnceWithTouchChannel()
        {
            var source = Source(1 << 8);
            var collider = ColliderOnLayer(8);
            var completions = 0;
            QuestSourceResult result = null;
            source.Terminated += emitted => { completions++; result = emitted; };
            source.TryActivate(Activation("touch-active"));

            Invoke(source, "OnTriggerEnter", collider);
            Invoke(source, "OnTriggerEnter", collider);

            Assert.AreEqual(1, completions);
            Assert.AreEqual(QuestSourceTerminalStatus.Completed, result.Status);
            Assert.AreEqual("touch", result.CompletionChannel);
        }

        [Test]
        public void DisallowedOrInactiveTrigger_DoesNotComplete()
        {
            var source = Source(1 << 8);
            var allowed = ColliderOnLayer(8);
            var disallowed = ColliderOnLayer(9);
            source.TryActivate(Activation("touch-active"));

            Invoke(source, "OnTriggerEnter", disallowed);
            Assert.AreEqual(QuestSourceState.Active, source.State);
            source.TryCancel(new QuestSourceCancellation("touch-active", "done"));
            Invoke(source, "OnTriggerEnter", allowed);
            Assert.AreEqual(QuestSourceState.Cancelled, source.State);
        }

        private TouchQuestSourceV2 Source(int mask)
        {
            var go = new GameObject("touch-source"); _objects.Add(go);
            var source = go.AddComponent<TouchQuestSourceV2>();
            Set(typeof(QuestSourceV2), source, "_bindingId", "touch");
            Set(typeof(TouchQuestSourceV2), source, "_interactorLayers", (LayerMask)mask);
            typeof(QuestSourceV2).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(source, null);
            return source;
        }
        private Collider ColliderOnLayer(int layer)
        {
            var go = new GameObject("interactor"); go.layer = layer; _objects.Add(go);
            return go.AddComponent<BoxCollider>();
        }
        private static QuestSourceActivation Activation(string id) => new QuestSourceActivation(id, System.DateTimeOffset.UtcNow, 1d);
        private static void Set(System.Type type, object target, string field, object value) => type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void Invoke(object target, string method, object argument) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, new[] { argument });
    }
}
