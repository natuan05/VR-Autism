using System.Collections;
using UnityEngine;

namespace VRAutism.Entities
{
    public class NPCLookAtPlayer : MonoBehaviour
    {
        [SerializeField] private Transform npcTransform;
        [SerializeField] private float slerpSpeed = 3f;

        private Coroutine _lookAtCoroutine;

        private void Start()
        {
            if (npcTransform == null)
            {
                npcTransform = transform;
            }
        }

        public void LookAtPlayerForDuration(float duration)
        {
            if (_lookAtCoroutine != null)
            {
                StopCoroutine(_lookAtCoroutine);
            }
            _lookAtCoroutine = StartCoroutine(SmoothLookAtPlayer(duration));
        }

        private IEnumerator SmoothLookAtPlayer(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (Camera.main == null) yield break;

                Vector3 lookDirection = Camera.main.transform.position - npcTransform.position;
                lookDirection.y = 0; // Giữ NPC đứng thẳng
                if (lookDirection != Vector3.zero)
                {
                    Quaternion targetRot = Quaternion.LookRotation(lookDirection);
                    npcTransform.rotation = Quaternion.Slerp(npcTransform.rotation, targetRot, Time.deltaTime * slerpSpeed);
                }
                elapsed += Time.deltaTime;
                yield return null;
            }
            _lookAtCoroutine = null;
        }
    }
}
