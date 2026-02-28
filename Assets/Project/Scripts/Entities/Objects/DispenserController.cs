using KBCore.Refs;
using UnityEngine;

namespace VRAutism.Entities
{
    public class DispenserController : MonoBehaviour
    {
        [SerializeField] private Animator animator;

        public void Push()
        {
            animator.SetTrigger("Push");
        }
    }

}
