using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FlockUnit : BaseMono
{
    [SerializeField] private float fovAngels;
    [SerializeField] private float smoothDamp;
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private Vector3[] directionsToCheckWhenAvoidance ;

    private List<FlockUnit> _cohesionNeighbors;
    private List<FlockUnit> _avoidanceNeighbors;
    private List<FlockUnit> _alignmentNeighbors;
    private Flock _assignedFlock;
    private Vector3 _currentVelocity;
    private float _speed;
    private Vector3 _currentObstacleAvoidanceVector;
    
    protected override void Initialize()
    {
        _cohesionNeighbors = new List<FlockUnit>();
        _avoidanceNeighbors = new List<FlockUnit>();
        _alignmentNeighbors = new List<FlockUnit>();
    }
    
    public void AssignFlock(Flock assignedFlock)
    {
        _assignedFlock = assignedFlock;
    }

    public void SetSpeed(float speed)
    {
        _speed = speed;
    }
    
    public void MoveUnit()
    {
        FindNeighbors();
        CalculateSpeed();
        
        var cohesionVector = CalculateCohesionVector() * _assignedFlock.CohesionWeight;
        var avoidanceVector = CalculateAvoidanceVector() * _assignedFlock.AvoidanceWeight;
        var alignmentVector = CalculateAlignmentVector() * _assignedFlock.AlignmentWeight;
        var boundsVector = CalculateBoundsVector() * _assignedFlock.BoundsWeight;
        var obstacleVector = CalculateObstacleAvoidanceVector() * _assignedFlock.ObstacleWeight;
        
        var moveVector = cohesionVector + avoidanceVector + alignmentVector + boundsVector + obstacleVector;
        
        moveVector = Vector3.SmoothDamp(transform.forward, moveVector, ref _currentVelocity, smoothDamp);
        moveVector = moveVector.normalized * _speed;

        if (moveVector == Vector3.zero)
        {
            if (transform.forward == Vector3.zero)
            {
                transform.forward = Vector3.forward;
            }
            
            moveVector = transform.forward.normalized * _speed;
        }
        
        transform.forward = moveVector;
        transform.position += moveVector * Time.deltaTime;
    }

    private void CalculateSpeed()
    {
        if (_cohesionNeighbors.Count == 0)
        {
            _speed = Mathf.Clamp(_speed, _assignedFlock.MinSpeed, _assignedFlock.MaxSpeed);
            return;
        }
        _speed = 0;
        foreach (var neighbor in _cohesionNeighbors)
        {
            _speed += neighbor._speed;
        }
        
        _speed /= _cohesionNeighbors.Count;
        
        _speed = Mathf.Clamp(_speed, _assignedFlock.MinSpeed, _assignedFlock.MaxSpeed);
    }

    private void FindNeighbors()
    {
        _cohesionNeighbors.Clear();
        _avoidanceNeighbors.Clear();
        _alignmentNeighbors.Clear();
        var allUnits = _assignedFlock.AllUnits;

        foreach (var currentUnit in allUnits)
        {
            if (currentUnit != this)
            {
                var currentNeighborDistanceSqr = (currentUnit.transform.position - transform.position).sqrMagnitude;

                if (currentNeighborDistanceSqr <= _assignedFlock.CohesionDistance * _assignedFlock.CohesionDistance)
                {
                    _cohesionNeighbors.Add(currentUnit);
                }
                
                if (currentNeighborDistanceSqr <= _assignedFlock.AvoidanceDistance * _assignedFlock.AvoidanceDistance)
                {
                    _avoidanceNeighbors.Add(currentUnit);
                }
                
                if (currentNeighborDistanceSqr <= _assignedFlock.AlignmentDistance * _assignedFlock.AlignmentDistance)
                {
                    _alignmentNeighbors.Add(currentUnit);
                }
                
            }
        }
    }

    private Vector3 CalculateCohesionVector()
    {
        var cohesionVector = Vector3.zero;
        var neighborsInFOV = 0;
        
        if (_cohesionNeighbors.Count == 0) return cohesionVector;

        foreach(var unit in _cohesionNeighbors)
        {
            if (IsInFOV(unit.transform.position))
            {
                neighborsInFOV++;
                cohesionVector += unit.transform.position;
            }
        }
        
        cohesionVector /= neighborsInFOV;
        cohesionVector -= transform.position;
        return cohesionVector.normalized;
    }

    private Vector3 CalculateAvoidanceVector()
    {
        var avoidanceVector = Vector3.zero;
        if (_avoidanceNeighbors.Count == 0) return avoidanceVector;
        var neighborsInFOV = 0;
        foreach (var unit in _avoidanceNeighbors)
        {
            if (IsInFOV(unit.transform.position))
            {
                neighborsInFOV++;
                avoidanceVector += (transform.position - unit.transform.position);
            }
        }
        avoidanceVector /= neighborsInFOV;
        return avoidanceVector.normalized;
    }

    private Vector3 CalculateAlignmentVector()
    {
        var alignmentVector = transform.forward;
        if (_alignmentNeighbors.Count == 0) return alignmentVector;
        var neighborsInFOV = 0;
        foreach (var unit in _alignmentNeighbors)
        {
            if (IsInFOV(unit.transform.position))
            {
                neighborsInFOV++;
                alignmentVector += unit.transform.forward;
            }
        }
        alignmentVector /= neighborsInFOV;
        return alignmentVector.normalized;
    }

    private Vector3 CalculateBoundsVector()
    {
        var offsetToCenter =_assignedFlock.transform.position - transform.position;
        var isNearCenter = offsetToCenter.sqrMagnitude >= _assignedFlock.BoundsDistance * 0.9f;
        return isNearCenter ? offsetToCenter.normalized : Vector3.zero;
    }

    private Vector3 CalculateObstacleAvoidanceVector()
    {
        var obstacleAvoidanceVector = Vector3.zero;
        
        if (Physics.Raycast(transform.position, transform.forward, out var hit, _assignedFlock.ObstacleDistance,
                obstacleMask))
        {
            obstacleAvoidanceVector = FindBestDirectionToAvoidObstacle();
        }
        else
        {
            _currentObstacleAvoidanceVector = Vector3.zero;
        }
        return obstacleAvoidanceVector;
    }

    private Vector3 FindBestDirectionToAvoidObstacle()
    {
        if (_currentObstacleAvoidanceVector != Vector3.zero)
        {
            if(!Physics.Raycast(transform.position, transform.forward, out var hit, _assignedFlock.ObstacleDistance, obstacleMask))
            {
                return _currentObstacleAvoidanceVector;
            }
        }
        
        var maxDistance = float.MinValue;
        var selectedDirection = Vector3.zero;

        foreach (var dir in directionsToCheckWhenAvoidance)
        {
            var currentDirection = transform.TransformDirection(dir.normalized);
            
            if (Physics.Raycast(transform.position, currentDirection, out var hit, _assignedFlock.ObstacleDistance,
                    obstacleMask))
            {
                var currentDistance = (hit.point - transform.position).sqrMagnitude;
                if (currentDistance > maxDistance)
                {
                    maxDistance = currentDistance;
                    selectedDirection = currentDirection;
                }
            }
            else
            {
                selectedDirection = currentDirection;
                _currentObstacleAvoidanceVector = currentDirection.normalized;
                return selectedDirection.normalized;
            }
        }
        return selectedDirection.normalized;
    }

    private bool IsInFOV(Vector3 position)
    {
        return Vector3.Angle(transform.forward, position - transform.position) <= fovAngels;
    }
}
