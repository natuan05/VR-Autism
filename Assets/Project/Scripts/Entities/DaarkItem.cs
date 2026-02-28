using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class DaarkItem : MonoBehaviour, IDaarkInteractable
{
    public bool IsInteracted { get; set; }
    private Transform targetHolder;
    private Collider collision;
    private Rigidbody rb;
    
    private void Awake()
    {
        collision = GetComponent<Collider>();
        rb = GetComponent<Rigidbody>();
    }

    public void PickUp(Transform holder)
    {
        IsInteracted = true;
        targetHolder = holder;
        collision.isTrigger = true;
        rb.isKinematic = true;
        rb.useGravity = false;

    }

    public void DropDown()
    {
        IsInteracted = false;
        collision.isTrigger = false;
        rb.isKinematic = false;
        rb.useGravity = true;
    }

    private void Update()
    {
        if (IsInteracted)
        {
            transform.position = targetHolder.position;
        }
    }
}
