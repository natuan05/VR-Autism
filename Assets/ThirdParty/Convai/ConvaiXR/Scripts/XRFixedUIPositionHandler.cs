using UnityEngine;

/// <summary>
/// Manages the UI element's position and rotation in a XR environment
/// so that it maintains a fixed position relative to the player camera.
/// </summary>
public class XRFixedUIPositionHandler : MonoBehaviour
{
    // Offset from the camera's position
    [SerializeField] private Vector3 _offset;
    // Speed of the interpolation for smooth movement
    [SerializeField] private float lerpSpeed;
    // Whether the UI is allowed to move along the X axis
    [SerializeField] private bool allowXMovement = true;
    // Whether the UI is allowed to move along the Y axis
    [SerializeField] private bool allowYMovement = true;
    // Whether the UI is allowed to move along the Z axis
    [SerializeField] private bool allowZMovement = true;

    // Reference to the main camera
    private Camera playerCamera;

    /// <summary>
    /// Initializes the reference to the main camera.
    /// </summary>
    private void Awake()
    {
        playerCamera = Camera.main;
    }

    /// <summary>
    /// Updates the UI's position and rotation each frame.
    /// </summary>
    private void LateUpdate()
    {
        UpdateUIPosition();
    }

    /// <summary>
    /// Calculates the target position for the UI element and smoothly moves it to that position.
    /// </summary>
    private void UpdateUIPosition()
    {
        Vector3 targetPosition = CalculateTargetPosition();
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * lerpSpeed);
        UpdateUIRotation();
    }

    /// <summary>
    /// Calculates the target position of the UI element relative to the player's camera.
    /// </summary>
    /// <returns>The calculated target position.</returns>
    private Vector3 CalculateTargetPosition()
    {
        Vector3 targetOffset = CalculateTargetOffset();
        Vector3 targetPosition = playerCamera.transform.position + targetOffset;

        // Apply fixed offsets if movement is not allowed
        if (!allowXMovement) targetPosition.x = _offset.x;
        if (!allowYMovement) targetPosition.y = _offset.y;
        if (!allowZMovement) targetPosition.z = _offset.z;

        return targetPosition;
    }

    /// <summary>
    /// Calculates the offset vector based on the allowed movement directions.
    /// </summary>
    /// <returns>The calculated offset vector.</returns>
    private Vector3 CalculateTargetOffset()
    {
        Vector3 targetOffset = Vector3.zero;

        if (allowXMovement)
            targetOffset += _offset.x * playerCamera.transform.right;

        if (allowYMovement)
            targetOffset += _offset.y * playerCamera.transform.up;

        if (allowZMovement)
            targetOffset += _offset.z * playerCamera.transform.forward;

        return targetOffset;
    }

    /// <summary>
    /// Updates the rotation of the UI element to face the camera.
    /// </summary>
    private void UpdateUIRotation()
    {
        Vector3 direction = transform.position - playerCamera.transform.position;
        transform.rotation = Quaternion.LookRotation(direction);
    }
}