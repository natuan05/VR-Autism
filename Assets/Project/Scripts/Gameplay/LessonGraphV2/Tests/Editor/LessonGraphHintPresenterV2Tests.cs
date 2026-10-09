using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VRAutism.Gameplay.LessonGraphV2.Presentation;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class LessonGraphHintPresenterV2Tests
    {
        [Test]
        public void SetProgress_UsesSpriteBackedRadialFillAndClearsOwnedUiInEditMode()
        {
            Assert.IsFalse(Application.isPlaying);
            var progressSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Project/UI/UI/Menu/rec.png");
            Assert.IsNotNull(progressSprite, "The legacy radial progress sprite must be available.");

            var template = CreateProgressTemplate(progressSprite);
            var anchorObject = new GameObject("ProgressAnchor");
            var presenterObject = new GameObject("LessonGraphHintPresenterV2Test");
            var presenter = presenterObject.AddComponent<LessonGraphHintPresenterV2>();
            typeof(LessonGraphHintPresenterV2)
                .GetField("_progressPrefab", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(presenter, template);

            try
            {
                var setProgress = typeof(LessonGraphHintPresenterV2)
                    .GetMethod("SetProgress", BindingFlags.Instance | BindingFlags.NonPublic);
                var progressRootField = typeof(LessonGraphHintPresenterV2)
                    .GetField("_progressRoot", BindingFlags.Instance | BindingFlags.NonPublic);
                var sliderField = typeof(LessonGraphHintPresenterV2)
                    .GetField("_progressSlider", BindingFlags.Instance | BindingFlags.NonPublic);

                foreach (var value in new[] { 0f, 0.5f, 1f })
                {
                    setProgress.Invoke(presenter, new object[] { anchorObject.transform, value });
                    var progressRoot = progressRootField.GetValue(presenter) as GameObject;
                    var slider = sliderField.GetValue(presenter) as Slider;
                    Assert.IsNotNull(progressRoot);
                    Assert.IsNotNull(slider);
                    Assert.IsTrue(progressRoot.activeSelf);
                    var fillImage = slider.fillRect.GetComponent<Image>();
                    Assert.AreSame(progressSprite, fillImage.sprite);
                    Assert.AreEqual(Image.Type.Filled, fillImage.type);
                    Assert.AreEqual(Image.FillMethod.Radial360, fillImage.fillMethod);
                    Assert.That(fillImage.fillAmount, Is.EqualTo(value).Within(0.001f));
                }

                var ownedProgressRoot = progressRootField.GetValue(presenter) as GameObject;
                Assert.DoesNotThrow(presenter.ClearPresentation);
                Assert.IsTrue(ownedProgressRoot == null, "EditMode teardown must destroy the presenter-owned widget immediately.");
            }
            finally
            {
                Object.DestroyImmediate(presenterObject);
                Object.DestroyImmediate(anchorObject);
                Object.DestroyImmediate(template);
            }
        }

        private static GameObject CreateProgressTemplate(Sprite progressSprite)
        {
            var template = new GameObject("LessonGraphHoldProgressTemplateV2", typeof(RectTransform), typeof(Canvas));
            var canvas = template.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = (RectTransform)template.transform;
            canvasRect.sizeDelta = new Vector2(1080f, 1080f);
            canvasRect.localScale = Vector3.one * 0.0002f;

            var sliderObject = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderObject.transform.SetParent(template.transform, false);
            ((RectTransform)sliderObject.transform).sizeDelta = new Vector2(540f, 540f);
            var slider = sliderObject.GetComponent<Slider>();

            var fillObject = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fillObject.transform.SetParent(sliderObject.transform, false);
            var fillRect = (RectTransform)fillObject.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fillImage = fillObject.GetComponent<Image>();
            fillImage.sprite = progressSprite;
            fillImage.color = new Color(0f, 0.5595119f, 1f, 1f);
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Radial360;

            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.fillRect = fillRect;
            template.SetActive(false);
            return template;
        }
    }
}
