using System.Linq;
using System.Reflection;
using Firebase.Firestore;
using NUnit.Framework;
using VRAutism.Cloud.Models;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class LessonTelemetrySerializationV2Tests
    {
        // These tests check Firestore annotations and model defaults only. Actual Firebase SDK conversion remains unverified and must be checked in Unity.

        [Test]
        public void NodeLogData_UsesExactFrozenFirestoreFieldNames()
        {
            string[] expected =
            {
                "event_id", "session_id", "run_id", "graph_id", "lesson_id",
                "launch_token", "lesson_voice_revision", "child_phrase_revision",
                "node_id", "node_type", "node_name", "node_index", "activation_id",
                "entered_at_utc", "exited_at_utc", "duration_seconds", "elapsed_seconds",
                "status", "completion_channel"
            };

            var actual = typeof(NodeLogData).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.GetCustomAttributes(typeof(FirestorePropertyAttribute), true).Length != 0)
                .Select(p => p.Name)
                .OrderBy(name => name)
                .ToArray();

            CollectionAssert.AreEquivalent(expected, actual);
        }

        [Test]
        public void QuestLogData_UsesExactLegacyFirestoreFieldNames()
        {
            string[] expected =
            {
                "index", "quest_name", "response_time", "completion_status",
                "hints_verbal", "hints_visual", "hints_physical", "response_time_from_hint"
            };

            var actual = typeof(QuestLogData).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.GetCustomAttributes(typeof(FirestorePropertyAttribute), true).Length != 0)
                .Select(p => p.Name)
                .OrderBy(name => name)
                .ToArray();

            CollectionAssert.AreEquivalent(expected, actual);
        }

        [Test]
        public void SessionData_DeclaresLegacyAndAdditiveNodeLogFirestoreFields()
        {
            string[] expected =
            {
                "session_id", "child_profile_id", "hosted_by", "lesson_id", "lesson_name",
                "level_name", "level_index", "device_id", "type", "start_time", "finish_time",
                "duration", "completion_status", "score", "video_url", "quest_logs", "node_logs"
            };

            var actual = typeof(SessionData).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.GetCustomAttributes(typeof(FirestorePropertyAttribute), true).Length != 0)
                .Select(p => p.Name)
                .OrderBy(name => name)
                .ToArray();

            CollectionAssert.AreEquivalent(expected, actual);
        }

        [Test]
        public void SessionData_NodeLogsCollectionStartsEmpty()
        {
            var session = new SessionData();

            Assert.That(session.node_logs, Is.Not.Null);
            Assert.That(session.node_logs, Is.Empty);
        }
    }
}
