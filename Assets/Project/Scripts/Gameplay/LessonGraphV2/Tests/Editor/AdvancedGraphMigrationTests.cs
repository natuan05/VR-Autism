using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Editor;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class AdvancedGraphMigrationTests
    {
        private const string TestFolder = "Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor";
        private string _sourcePath;
        private string _copyPath;
        private LessonGraph _source;

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_copyPath)) AssetDatabase.DeleteAsset(_copyPath);
            if (!string.IsNullOrEmpty(_sourcePath)) AssetDatabase.DeleteAsset(_sourcePath);
            _source = null;
            _sourcePath = null;
            _copyPath = null;
        }

        private LessonGraph CreateSource(LessonNodeData node, int version = 1)
        {
            _source = ScriptableObject.CreateInstance<LessonGraph>();
            _sourcePath = TestFolder + "/__TempMigrationSource_" + System.Guid.NewGuid().ToString("N") + ".asset";
            _source.Editor_SetSchemaVersion(version);
            _source.Editor_SetEntryNodeId(node.Id);
            _source.Editor_SetNodes(new List<LessonNodeData> { node });
            _source.Editor_SetEdges(new List<LessonEdgeData>());
            AssetDatabase.CreateAsset(_source, _sourcePath);
            AssetDatabase.SaveAssets();
            return _source;
        }

        [Test]
        public void MigrationCreatesValidCopyAndPreservesSchemaOneSource()
        {
            CreateSource(new LessonNodeData("entry", NodeType.Wait, new WaitNodeConfig(1)));
            var success = LessonGraphSchemaMigration.TryCreateSchemaTwoCopy(_source, out var copy, out var result, out var error);
            try
            {
                _copyPath = AssetDatabase.GetAssetPath(copy);
                Assert.IsTrue(success, error);
                Assert.IsTrue(result.IsValid, result.ToString());
                Assert.AreEqual(1, _source.SchemaVersion);
                Assert.AreEqual(2, copy.SchemaVersion);
                Assert.AreNotEqual(AssetDatabase.GetAssetPath(_source), AssetDatabase.GetAssetPath(copy));
            }
            finally { if (!string.IsNullOrEmpty(_copyPath)) AssetDatabase.DeleteAsset(_copyPath); _copyPath = null; }
        }

        [Test]
        public void MigrationDoesNotSaveInvalidCandidate()
        {
            CreateSource(new LessonNodeData("checkpoint", NodeType.Checkpoint,
                new CheckpointNodeConfig("checkpoint-id")));
            var success = LessonGraphSchemaMigration.TryCreateSchemaTwoCopy(_source, out var copy, out var result, out _);
            Assert.IsFalse(success);
            Assert.IsNull(copy);
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(1, _source.SchemaVersion);
        }

        [Test]
        public void MigrationRejectsUnsupportedSourceVersionWithoutMutation()
        {
            CreateSource(new LessonNodeData("entry", NodeType.Wait, new WaitNodeConfig(1)), 3);
            Assert.IsFalse(LessonGraphSchemaMigration.TryCreateSchemaTwoCopy(_source, out var copy, out _, out _));
            Assert.IsNull(copy);
            Assert.AreEqual(3, _source.SchemaVersion);
        }
    }
}
