using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRAutism.Gameplay.LessonGraphV2.Presentation;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class BathroomV2BubbleHintSceneTests
    {
        private const string LegacyScenePath = "Assets/Project/Scenes/Bathroom.unity";
        private const string V2ScenePath = "Assets/Project/Scenes/Bathroom-V2.unity";
        private const string TemplateName = "BubbleQuestionHintTemplateV2";
        private const string ImageSpriteGuid = "92e2e115ac045a545975fbff4eea2b07";

        private static readonly (string Source, string Clone)[] SourceCloneIds =
        {
            ("721296230", "6300000000000004001"),
            ("721296231", "6300000000000004002"),
            ("721296232", "6300000000000004003"),
            ("721296233", "6300000000000004004"),
            ("721296234", "6300000000000004005"),
            ("721296235", "6300000000000004006"),
            ("191985541", "6300000000000004007"),
            ("191985542", "6300000000000004008"),
            ("191985543", "6300000000000004009"),
            ("191985544", "6300000000000004010")
        };

        [Test]
        public void HintPresenter_UsesInactiveLegacyBubbleQuestionTemplate()
        {
            var scene = EditorSceneManager.OpenPreviewScene(V2ScenePath);
            try
            {
                var root = scene.GetRootGameObjects().SingleOrDefault(item => item.name == TemplateName);
                Assert.That(root, Is.Not.Null);
                Assert.That(root.activeSelf, Is.False);
                Assert.That(root.transform.parent, Is.Null);

                var presenter = scene.GetRootGameObjects()
                    .SelectMany(item => item.GetComponentsInChildren<LessonGraphHintPresenterV2>(true))
                    .Single();
                var serializedPresenter = new SerializedObject(presenter);
                var bubblePrefab = serializedPresenter.FindProperty("_bubblePrefab").objectReferenceValue;
                Assert.That(bubblePrefab, Is.SameAs(root));

                var canvas = root.GetComponent<Canvas>();
                Assert.That(canvas, Is.Not.Null);
                Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.WorldSpace));
                Assert.That(root.GetComponent<CanvasScaler>(), Is.Not.Null);
                Assert.That(root.GetComponent<GraphicRaycaster>(), Is.Not.Null);
                Assert.That(root.GetComponents<Component>(), Has.Length.EqualTo(5));
                Assert.That(root.transform.localScale, Is.EqualTo(Vector3.one * 0.0002f));
                Assert.That(root.transform.localPosition.z, Is.EqualTo(-0.394f).Within(0.00001f));
                Assert.That(((RectTransform)root.transform).sizeDelta, Is.EqualTo(new Vector2(1080, 1080)));

                var imageObject = root.transform.GetChild(0).gameObject;
                Assert.That(imageObject.name, Is.EqualTo("Image"));
                Assert.That(imageObject.GetComponents<Component>(), Has.Length.EqualTo(3));
                Assert.That(((RectTransform)imageObject.transform).sizeDelta, Is.EqualTo(new Vector2(256, 256)));
                var image = imageObject.GetComponent<Image>();
                Assert.That(image, Is.Not.Null);
                Assert.That(image.preserveAspect, Is.True);
                Assert.That(image.color, Is.EqualTo(Color.white));
                var spritePath = AssetDatabase.GUIDToAssetPath(ImageSpriteGuid);
                var legacySprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
                Assert.That(legacySprite, Is.Not.Null, "The legacy bubble-question sprite must be imported.");
                Assert.That(image.sprite, Is.SameAs(legacySprite));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void HintTemplate_MatchesEveryLegacyBubbleQuestionDocumentExceptRequestedOverrides()
        {
            var legacy = File.ReadAllText(LegacyScenePath);
            var current = File.ReadAllText(V2ScenePath);
            foreach (var id in SourceCloneIds)
            {
                var legacyDocument = NormalizeLegacyDocument(GetDocument(legacy, id.Source));
                var clonedDocument = NormalizeDocumentWhitespace(GetDocument(current, id.Clone));
                Assert.That(clonedDocument, Is.EqualTo(legacyDocument), $"Copied document {id.Source} differs after ID normalization.");
            }
        }

        [Test]
        public void BathroomV2Scene_HasUniqueDocumentIdsAndClosedLocalPointers()
        {
            var sceneText = File.ReadAllText(V2ScenePath);
            var documentIds = Regex.Matches(sceneText, @"(?m)^--- !u!\d+ &(\d+)")
                .Cast<Match>()
                .Select(match => match.Groups[1].Value)
                .ToArray();
            var declaredIds = new HashSet<string>(documentIds);
            Assert.That(declaredIds.Count, Is.EqualTo(documentIds.Length), "Scene document IDs must be unique.");

            var localPointers = Regex.Matches(sceneText, @"\{fileID:\s*(-?\d+)(?:,\s*guid:\s*[a-fA-F0-9]+)?(?:,\s*type:\s*\d+)?\}")
                .Cast<Match>()
                .Where(match => !match.Value.Contains("guid:"))
                .Select(match => match.Groups[1].Value)
                .Where(id => id != "0");
            var unresolved = localPointers.Where(id => !declaredIds.Contains(id)).Distinct().ToArray();
            Assert.That(unresolved, Is.Empty, "Every local scene PPtr, including stripped objects, must resolve to a scene document.");

            Assert.That(sceneText, Does.Contain("_bubblePrefab: {fileID: 6300000000000004001}"));
            Assert.That(sceneText, Does.Not.Contain("targetCamera: {fileID: 1402748691}"));
        }

        private static string GetDocument(string sceneText, string fileId)
        {
            var match = Regex.Match(sceneText,
                @"(?ms)^--- !u!\d+ &" + Regex.Escape(fileId) + @"\r?\n.*?(?=^--- !u!|\z)");
            Assert.That(match.Success, Is.True, $"Scene document {fileId} was not found.");
            return match.Value.Replace("\r", string.Empty).Trim();
        }

        private static string NormalizeLegacyDocument(string document)
        {
            var normalized = document;
            foreach (var id in SourceCloneIds)
                normalized = Regex.Replace(normalized, @"(?<!\d)" + id.Source + @"(?!\d)", id.Clone);

            normalized = normalized.Replace("m_Name: BubbleQuestion", "m_Name: " + TemplateName)
                .Replace("m_Father: {fileID: 492935883}", "m_Father: {fileID: 0}")
                .Replace("targetCamera: {fileID: 1402748691}", "targetCamera: {fileID: 0}");
            return NormalizeDocumentWhitespace(normalized);
        }

        private static string NormalizeDocumentWhitespace(string document)
        {
            var lines = document.Replace("\r", string.Empty).Split('\n');
            for (var index = 0; index < lines.Length; index++)
                lines[index] = Regex.Replace(lines[index], @"[ \t]+$", string.Empty);
            return string.Join("\n", lines).Trim();
        }
    }
}
