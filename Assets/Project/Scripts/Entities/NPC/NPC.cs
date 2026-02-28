using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NPC : MonoBehaviour
{
    [SerializeField] private Transform visual;
    [SerializeField] private NPCAction action;
    private Animator _animator;

    private void Awake()
    {
        _animator = visual.GetComponent<Animator>();
        SetAction(action);
    }

    // Update is called once per frame
    private void Update()
    {
        
    }

    public void SetAction(NPCAction action)
    {
        switch (action)
        {
            case NPCAction.PickingFruit:
                _animator.SetTrigger("Pick Fruit");
                break;

            case NPCAction.Walking:
                _animator.SetTrigger("Walking");
                break;
            
            case NPCAction.Waving:
                _animator.SetTrigger("Waving");
                break;

            case NPCAction.Greeting:
                _animator.SetTrigger("Greeting");
                break;
            
            case NPCAction.Talking:
                _animator.SetTrigger("Talking");
                break;

            default:
                _animator.SetTrigger("Idle");
                break;

        }
    }
}


public enum NPCAction
{
    PickingFruit,
    Walking,
    Waving,
    Talking,
    Greeting,
}