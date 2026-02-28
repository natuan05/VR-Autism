using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerInteract : MonoBehaviour
{
    [SerializeField] private float interactRange = 2f;

    private void Update()
    {
        var colliderArray = Physics.OverlapSphere(transform.position, interactRange);
        var showChatInteract = false;
        
    }
}
