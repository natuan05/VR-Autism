using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Flock : BaseMono
{
    [Header("Spawn Setup")]
    [SerializeField] private FlockUnit flockUnitPrefab;
    [SerializeField] private int flockSize;
    [SerializeField] private Vector3 spawnBounds;
    
    [Header("Speed Setup")]
    [Range(0, 10)]
    [SerializeField] private float minSpeed;
    [Range(0, 10)]
    [SerializeField] private float maxSpeed;
    
    [Header("Detection Setup")] 
    [Range(0, 10)]
    [SerializeField] private float cohesionDistance;
    [Range(0, 10)]
    [SerializeField] private float avoidanceDistance;
    [Range(0, 10)]
    [SerializeField] private float alignmentDistance;
    [Range(0, 100)]
    [SerializeField] private float boundsDistance;
    [Range(0, 10)]
    [SerializeField] private float obstacleDistance;

    public float CohesionDistance => cohesionDistance;
    public float AvoidanceDistance => avoidanceDistance;
    public float AlignmentDistance => alignmentDistance;
    public float BoundsDistance => boundsDistance;
    public float ObstacleDistance => obstacleDistance;
    
    
    [Header("Behaviour Weights")] 
    [Range(0, 10)]
    [SerializeField] private float cohesionWeight;
    [Range(0, 10)]
    [SerializeField] private float avoidanceWeight;
    [Range(0, 10)]
    [SerializeField] private float alignmentWeight;
    [Range(0, 10)]
    [SerializeField] private float boundsWeight;
    [Range(0, 10)]
    [SerializeField] private float obstacleWeight;
    

    public float CohesionWeight => cohesionWeight;
    public float AvoidanceWeight => avoidanceWeight;
    public float AlignmentWeight => alignmentWeight;
    public float BoundsWeight => boundsWeight;
    public float ObstacleWeight => obstacleWeight;
    public float MaxSpeed => maxSpeed;
    public float MinSpeed => minSpeed;

    public FlockUnit[] AllUnits { get; private set; }

    protected override void Initialize()
    {
        base.Initialize();
        GenerateUnits();
    }

    protected override void Tick()
    {
        base.Tick();

        foreach (var unit in AllUnits)
        {
           unit.MoveUnit();
        }
    }

    private void GenerateUnits()
    {
        AllUnits = new FlockUnit[flockSize];
        for (var i = 0; i < flockSize; i++)
        {
            var randomVector = Random.insideUnitSphere;
            randomVector = new Vector3(randomVector.x * spawnBounds.x, randomVector.y * spawnBounds.y, randomVector.z * spawnBounds.z);
            var spawnPosition = transform.position + randomVector;
            var rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
            
            AllUnits[i] = Instantiate(flockUnitPrefab, spawnPosition, rotation, transform);
            AllUnits[i].AssignFlock(this);
            AllUnits[i].SetSpeed(Random.Range(minSpeed, maxSpeed));
        }
    }
}
