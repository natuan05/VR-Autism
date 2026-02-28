using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace VRAutism.Quests
{
    public class ArrowPath : MonoBehaviour
    {
        public bool showArrows;
        public Transform target;
        [SerializeField] Transform startPoint;
        [SerializeField] LineRenderer lineRenderer;
    
        private NavMeshPath path;
    
        void Update()
        {
            GeneratePath();
        }
    
        void Start()
        {
            path = new NavMeshPath();
            lineRenderer.positionCount = 0;
        }

        void GeneratePath()
        {
            lineRenderer.gameObject.SetActive(showArrows);
            
            if (target != null && NavMesh.CalculatePath(startPoint.position, target.position, NavMesh.AllAreas, path))
            {
                lineRenderer.positionCount = path.corners.Length;
                lineRenderer.SetPositions(path.corners);
            }
        }
        
       
    }

}
