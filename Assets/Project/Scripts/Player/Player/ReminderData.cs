using VRAutism.Core;
using UnityEngine;


[CreateAssetMenu(fileName = "ReminderData", menuName = "Data/ReminderData")]
public class ReminderData : ScriptableObject
{
    [SerializeField] public AudioClip[] audioClips;
}
