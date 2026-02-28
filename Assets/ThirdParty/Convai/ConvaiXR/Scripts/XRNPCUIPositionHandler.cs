using Convai.Scripts.Runtime.Core;
using UnityEngine;

/// <summary>
/// Handles the UI transformation for XR interactions, adjusting the UI position based on the player's camera distance from an NPC.
/// </summary>
public class XRNPCUIPositionHandler : MonoBehaviour
{
    // Speed at which the UI lerps to its new position.
    [SerializeField] private float _lerpSpeed;
    // Offset from the NPC position.
    [SerializeField] private Vector3 _offset;
    // Distance threshold for switching offsets.
    [SerializeField] private float _cameraDistanceThreshold;
    // Reference to the main camera in the scene.
    private Camera _playerCamera;
    // Reference to the currently active NPC.
    private ConvaiNPC _currentNPC;

    /// <summary>
    /// Subscribes to the active NPC change event when the script is enabled.
    /// </summary>
    private void OnEnable()
    {
        if (ConvaiNPCManager.Instance != null)
        {
            ConvaiNPCManager.Instance.OnActiveNPCChanged += OnActiveNPCChanged;
            _currentNPC = ConvaiNPCManager.Instance.activeConvaiNPC;
        }
    }

    /// <summary>
    /// Unsubscribes from the active NPC change event when the script is disabled.
    /// </summary>
    private void OnDisable()
    {
        if (ConvaiNPCManager.Instance != null)
        {
            ConvaiNPCManager.Instance.OnActiveNPCChanged -= OnActiveNPCChanged;
        }
    }

    /// <summary>
    /// Gets the main camera reference.
    /// </summary>
    private void Start()
    {
        _playerCamera = Camera.main;
    }

    /// <summary>
    /// Updates the current NPC and sets the UI position when the active NPC changes.
    /// </summary>
    /// <param name="newNPC">The new active NPC.</param>
    private void OnActiveNPCChanged(ConvaiNPC newNPC)
    {
        _currentNPC = newNPC;
        if (_currentNPC != null && _playerCamera != null)
        {
            SetUIPosition();
        }
    }

    /// <summary>
    /// Updates the UI position and rotation to face the camera in each frame.
    /// </summary>
    private void LateUpdate()
    {
        if (_currentNPC != null)
        {
            UpdateUIPosition();
            FaceCamera();
        }
    }
    
    /// <summary>
    /// Sets the UI position based on the distance from the player camera to the NPC.
    /// </summary>
    private void SetUIPosition()
    {
        Transform npcTransform = _currentNPC.transform;
        Vector3 targetPosition = CalculateTargetPosition(npcTransform);
        transform.position = targetPosition;
    }

    /// <summary>
    /// Smoothly updates the UI position using linear interpolation (Lerp).
    /// </summary>
    private void UpdateUIPosition()
    {
        Transform npcTransform = _currentNPC.transform;
        Vector3 targetPosition = CalculateTargetPosition(npcTransform);
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * _lerpSpeed);
    }

    /// <summary>
    /// Calculates the target position of the UI based on the NPC's position and the distance to the player camera.
    /// </summary>
    /// <param name="npcTransform">The transform of the NPC.</param>
    /// <returns>The target position for the UI.</returns>
    private Vector3 CalculateTargetPosition(Transform npcTransform)
    {
        Vector3 leftOffset = new Vector3(-_offset.x, _offset.y, _offset.z);
        Vector3 rightOffset = new Vector3(_offset.x, _offset.y, _offset.z);

        Vector3 leftOffsetPosition = npcTransform.position + npcTransform.TransformDirection(leftOffset);
        Vector3 rightOffsetPosition = npcTransform.position + npcTransform.TransformDirection(rightOffset);

        float distanceToLeftOffset = Vector3.Distance(leftOffsetPosition, _playerCamera.transform.position);
        float distanceToRightOffset = Vector3.Distance(rightOffsetPosition, _playerCamera.transform.position);

        Vector3 dynamicOffset = DetermineDynamicOffset(distanceToLeftOffset, distanceToRightOffset);

        return npcTransform.position + npcTransform.TransformDirection(dynamicOffset);
    }

    /// <summary>
    /// Determines the appropriate dynamic offset based on the distances from the camera to the left and right offsets.
    /// </summary>
    /// <param name="distanceToLeftOffset">Distance to the left offset position.</param>
    /// <param name="distanceToRightOffset">Distance to the right offset position.</param>
    /// <returns>The chosen offset vector.</returns>
    private Vector3 DetermineDynamicOffset(float distanceToLeftOffset, float distanceToRightOffset)
    {
        Vector3 leftOffset = new Vector3(-_offset.x, _offset.y, _offset.z);
        Vector3 rightOffset = new Vector3(_offset.x, _offset.y, _offset.z);

        float threshold = 0.5f;

        if (distanceToLeftOffset < _cameraDistanceThreshold && distanceToRightOffset < _cameraDistanceThreshold)
        {
            float difference = Mathf.Abs(distanceToLeftOffset - distanceToRightOffset);
            return difference > threshold
                ? (distanceToLeftOffset > distanceToRightOffset ? leftOffset : rightOffset)
                : leftOffset;
        }
        else
        {
            return distanceToLeftOffset >= _cameraDistanceThreshold ? leftOffset : rightOffset;
        }
    }

    /// <summary>
    /// Makes the UI face the camera.
    /// </summary>
    private void FaceCamera()
    {
        Vector3 direction = transform.position - _playerCamera.transform.position;
        transform.rotation = Quaternion.LookRotation(direction);
    }
}