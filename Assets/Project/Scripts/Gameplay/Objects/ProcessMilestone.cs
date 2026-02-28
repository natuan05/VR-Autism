using VRAutism.Core;
using UnityEngine;

public class ProcessMilestone : MonoBehaviour
{
    [SerializeField] private int processID;
    [SerializeField] private int time;
    
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Character"))
        {
            this.SendEvent(EventID.OnTriggerProcessEnter, new Process()
            {
                ID = processID,
                Time = time,
                Position = transform.position,
            });
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Character"))
        {
            this.SendEvent(EventID.OnTriggerProcessExit, new Process()
            {
                ID = processID,
                Time = time,
                Position = transform.position,
            });
        }
    }
}
