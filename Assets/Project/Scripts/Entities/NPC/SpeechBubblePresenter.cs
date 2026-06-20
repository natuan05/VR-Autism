using UnityEngine;

namespace VRAutism.Entities
{
    public class SpeechBubblePresenter : MonoBehaviour
    {
        [SerializeField] private GameObject speechBubblePrefab;
        [SerializeField] private Transform bubbleAnchor;

        private GameObject _activeBubble;

        public void Show(string text, float duration = 5.0f)
        {
            Hide();

            if (speechBubblePrefab == null || bubbleAnchor == null)
            {
                Debug.LogWarning("[SpeechBubblePresenter] Prefab or Anchor is not assigned. Cannot show speech bubble.");
                return;
            }

            _activeBubble = Instantiate(speechBubblePrefab, bubbleAnchor.position, Quaternion.identity);
            
            var tmpText = _activeBubble.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (tmpText != null)
            {
                tmpText.text = text;
            }
            else
            {
                Debug.LogWarning("[SpeechBubblePresenter] TMPro.TextMeshProUGUI not found in speech bubble prefab.");
            }

            if (_activeBubble.GetComponent<BillboardEffect>() == null)
            {
                _activeBubble.AddComponent<BillboardEffect>();
            }

            Destroy(_activeBubble, duration);
        }

        public void Hide()
        {
            if (_activeBubble != null)
            {
                Destroy(_activeBubble);
                _activeBubble = null;
            }
        }
    }
}
