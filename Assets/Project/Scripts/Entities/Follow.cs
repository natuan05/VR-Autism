using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Follow : MonoBehaviour
{
    [SerializeField] private Transform followPosition;
    private void Start()
    {
        
    }

    // Update is called once per frame
    private void Update()
    {
        transform.position = followPosition.position;
    }
}
