using System.Collections;
using System.Collections.Generic;
using VRAutism.Core;
using KBCore.Refs;
using UnityEngine;
using UnityEngine.Serialization;

public class WaterTap : MonoBehaviour
{

    [SerializeField] private Animator tap;

    public ParticleSystem RunningWater;
    
    private bool inReach;
    private bool isOpen;
    private bool isClosed;

    public void ReachSet(bool inUse)
    {
        if (inUse)
        {
            if (isClosed)
            {
                inReach = true;
            }

            if (isOpen)
            {
                inReach = true;
            }
        }
        else
        {
            inReach = false;
        }
        

    }

    void Start()
    {
        inReach = false;
        isClosed = true;
        isOpen = false;
        RunningWater.Stop();
    }

    // void Update()
    // {
    //     Ray ray = Camera.main.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0f));
    //     RaycastHit hit;
    //     if (Physics.Raycast(ray, out hit, 2.5f/*, layerMask*/))
    //     {
    //         Debug.DrawRay(ray.origin, ray.direction * hit.distance, Color.red);
    //         if (hit.collider.CompareTag("Interactable") && hit.distance <= 2.5f)
    //         {
    //             ReachSet(true);
    //             Debug.Log("Object is Interactable");
    //
    //         }
    //         else
    //         {
    //             ReachSet(false);
    //         }
    //
    //     }
    //     else
    //     {
    //         ReachSet(false);
    //
    //     }
    // }

    public void Interact()
    {
        if (inReach && isClosed)
        {
            tap.SetBool("Open", true);
            tap.SetBool("Closed", false);
            this.SendEvent(EventID.PlaySoundLoop, TypeSound.WaterSound);
            isOpen = true;
            isClosed = false;
            RunningWater.gameObject.SetActive(true);
            RunningWater.Play();
        }

        else if (inReach && isOpen)
        {
            tap.SetBool("Open", false);
            tap.SetBool("Closed", true);
            this.SendEvent(EventID.PauseSound);
            isClosed = true;
            isOpen = false;
            RunningWater.Stop();
        }
    }
}
