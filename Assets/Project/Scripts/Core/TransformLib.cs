using UnityEngine;

namespace VRAutism.Core
{
    public class TransformLib : MonoBehaviour {
        public static void SetStretchAnchorAll(RectTransform t)
        {
            t.pivot = Vector2.one * 0.5f;
            t.anchorMin = Vector2.zero;
            t.anchorMax = Vector2.one;
            t.anchoredPosition = Vector2.zero;
            t.sizeDelta = Vector2.zero;
        }

        public static void SetStretchAnchorLeft(RectTransform t)
        {
            SetStretchAnchorToSide(t, Vector2.up);
        }
        public static void SetStretchAnchorRight(RectTransform t)
        {
            SetStretchAnchorToFarSide(t, Vector2.right);
        }
        public static void SetStretchAnchorTop(RectTransform t)
        {
            SetStretchAnchorToFarSide(t, Vector2.up);
        }
        public static void SetStretchAnchorBottom(RectTransform t)
        {
            SetStretchAnchorToSide(t, Vector2.right);
        }
        static void SetStretchAnchorToSide(RectTransform t, Vector2 stretch)
        {
            var old_size = t.rect.size;
            var perpendicular = Vector2.one - stretch;
            t.pivot = stretch * 0.5f;
            t.anchorMin = Vector2.zero;
            t.anchorMax = stretch;
            t.anchoredPosition = Vector2.zero;
            t.sizeDelta = Vector2.Scale(perpendicular, old_size);
        }
        static void SetStretchAnchorToFarSide(RectTransform t, Vector2 stretch)
        {
            var old_size = t.rect.size;
            t.pivot = (Vector2.one + stretch) * 0.5f;
            t.anchorMin = stretch;
            t.anchorMax = Vector2.one;
            t.anchoredPosition = Vector2.zero;
            t.sizeDelta = Vector2.Scale(stretch, old_size);
        }
    }
}
