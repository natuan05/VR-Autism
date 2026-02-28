using System;
using System.Collections;
using System.Collections.Generic;
using VRAutism.Core;
using UnityEngine;

public class Door : MonoBehaviour
{
    [SerializeField] private Transform visual;
    [SerializeField] private Collider openBlock;
    [SerializeField] private GameObject interactUI;
    
    private Animator _animator;
    private bool _canInteract;
    private State _state;
    
    public enum State
    {
        Open,
        Close
    }
    
    private void Awake()
    {
        _animator = visual.GetComponent<Animator>();
        _state = State.Close;
        interactUI.SetActive(false);
        this.SubscribeListener(EventID.ToggleTheDoor, (param) => { ToggleTheDoor();});
        
    }

    public void ToggleTheDoor()
    {
        if (_state == State.Close) Open();
        else Close();
        InteractController.Inst.HideDown();
    }

    private void Open()
    {
        _animator.Play("Open");
        openBlock.isTrigger = true;
        _state = State.Close;
    }

    private void Close()
    {
        _animator.Play("Close");
        openBlock.isTrigger = false;
        _state = State.Open;
    }

    private void OnTriggerEnter(Collider other)
    {
        // InteractController.Inst.ShowUp();
        // // interactUI.transform.localScale = Vector3.one;
        // interactUI.SetActive(true);
        // _canInteract = true;
        ToggleTheDoor();
    }

    private void OnTriggerExit(Collider other)
    {
        // InteractController.Inst.HideDown();
        // _canInteract = false;
        // interactUI.SetActive(true);
        ToggleTheDoor();
    }

    private void Update()
    {
        if (_canInteract)
        {
            if (Input.GetKeyUp(KeyCode.E))
            {
                if (_state == State.Close) Open();
                else Close();
                InteractController.Inst.HideDown(); 
            }
        }
    }
}
