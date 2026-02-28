using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Controls hand animations based on user input, managing trigger and grip actions through Unity's Input System.
/// </summary>
public class HandAnimationController : MonoBehaviour
{
    // Input action for the trigger button
    [SerializeField] private InputActionProperty _triggerAction;
    // Input action for the grip button
    [SerializeField] private InputActionProperty _gripAction;
    // Controls animations for the hand
    private Animator _animator;
    // Identifier for the trigger animation parameter
    private int _triggerAnimationParameterID;
    // Identifier for the grip animation parameter
    private int _gripAnimationParameterID;

    /// <summary>
    /// Initializes necessary components and variables.
    /// </summary>
    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _triggerAnimationParameterID = Animator.StringToHash("Trigger");
        _gripAnimationParameterID = Animator.StringToHash("Grip");
    }

    /// <summary>
    /// Subscribes to input events when the script is enabled.
    /// </summary>
    private void OnEnable()
    {
        _triggerAction.action.performed += XRController_SelectAction_Performed;
        _gripAction.action.performed += XRController_ActivateAction_Performed;
        _triggerAction.action.canceled += XRController_SelectAction_Canceled;
        _gripAction.action.canceled += XRController_ActivateAction_Canceled;
    }

    /// <summary>
    /// Unsubscribes from input events to prevent issues when the script is disabled.
    /// </summary>
    private void OnDisable()
    {
        _triggerAction.action.performed -= XRController_SelectAction_Performed;
        _gripAction.action.performed -= XRController_ActivateAction_Performed;
        _triggerAction.action.canceled -= XRController_SelectAction_Canceled;
        _gripAction.action.canceled -= XRController_ActivateAction_Canceled;
    }

    /// <summary>
    /// Handles trigger input to control the corresponding animation.
    /// </summary>
    private void XRController_SelectAction_Performed(InputAction.CallbackContext obj)
    {
        float triggerInputValue = obj.ReadValue<float>();
        _animator.SetFloat(_triggerAnimationParameterID, triggerInputValue);
    }

    /// <summary>
    /// Handles grip input to control the corresponding animation.
    /// </summary>
    private void XRController_ActivateAction_Performed(InputAction.CallbackContext obj)
    {
        float gripInputValue = obj.ReadValue<float>();
        _animator.SetFloat(_gripAnimationParameterID, gripInputValue);
    }

    /// <summary>
    /// Resets trigger animation when the trigger action is canceled.
    /// </summary>
    private void XRController_SelectAction_Canceled(InputAction.CallbackContext obj)
    {
        float triggerInputValue = 0;
        _animator.SetFloat(_triggerAnimationParameterID, triggerInputValue);
    }

    /// <summary>
    /// Resets grip animation when the grip action is canceled.
    /// </summary>
    private void XRController_ActivateAction_Canceled(InputAction.CallbackContext obj)
    {
        float gripInputValue = 0;
        _animator.SetFloat(_gripAnimationParameterID, gripInputValue);
    }
}