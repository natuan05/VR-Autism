using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NPCInteraction : MonoBehaviour
{
    [SerializeField] Waypoints waypoints;
    [Header("Speed Attribute")]
    [SerializeField] private float speed = 2;
    [SerializeField] private float rotateSpeed = 1;
    [SerializeField] private bool isLoop = true;
    [SerializeField] private Transform visual;

    int index = 0;
    private State currentState;
    

    private void Start()
    {
        currentState = State.Move;
    }

    private void Update()
    {
        switch (currentState)
        {
            case State.Move:
                MoveOnWaypoints();
                break;
            case State.PickFruits:
                break;
            case State.Talk:
                break;
        }

    }

    private void MoveOnWaypoints()
    {
        if (waypoints.waypoints.Count == 0) return;

        Vector3 destination = waypoints.waypoints[index].point.transform.position;
        transform.position = Vector3.MoveTowards(transform.position, destination, speed * Time.deltaTime);

        Vector3 moveDir = (destination - transform.position).normalized;
        moveDir.y = 0;

        if (Vector3.Distance(transform.position, destination) <= 0.05f)
        {
            if (index < waypoints.waypoints.Count - 1)
            {
                index++;
                if (waypoints.waypoints[index].isChangeState)
                {
                    SetState(waypoints.waypoints[index].state);
                }
                moveDir = (destination - transform.position).normalized;
                moveDir.y = 0;
            }
            else if (isLoop)
            {
                index = 0;
            }
        }

        visual.forward = Vector3.Lerp(visual.forward, moveDir, rotateSpeed * Time.deltaTime);

    }

    public void SetState(State state)
    {
        currentState = state;

        switch (state)
        {
            case State.Move:
                GetComponent<NPC>().SetAction(NPCAction.Walking);
                break;
            case State.PickFruits:
                GetComponent<NPC>().SetAction(NPCAction.PickingFruit);
                StartCoroutine(SetToDefaultState(waypoints.waypoints[index].time));
                break;
            case State.Talk:
                GetComponent<NPC>().SetAction(NPCAction.Talking);
                break;
        }
    }

    private IEnumerator SetToDefaultState(float time)
    {
        yield return new WaitForSeconds(time);
        SetState(State.Move);
    }

    public enum State
    {
        Move,
        PickFruits,
        Talk
    }
}

[Serializable]
public class Waypoints
{
    public List<Waypoint> waypoints = new List<Waypoint>();
}



[Serializable]
public class Waypoint
{
    public GameObject point;
    public NPCInteraction.State state;
    public bool isChangeState;
    public float time;
}


