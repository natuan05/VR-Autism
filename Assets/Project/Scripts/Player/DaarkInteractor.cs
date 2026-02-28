using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IDaarkInteractable
{ 
    public bool IsInteracted { get; set; }
    public void PickUp(Transform holder);
    public void DropDown();

}

public class DaarkInteractor : MonoBehaviour
{
    [SerializeField] private Transform interactorSource;
    [SerializeField] private float interactRange;
    [SerializeField] private Transform holder;
    
    private IDaarkInteractable interactObject;
    
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F))
        {
            if (interactObject != null)
            {
                interactObject.DropDown();
                interactObject = null;
            }
            else
            {
                var r = new Ray(interactorSource.position, interactorSource.forward);
                if (Physics.Raycast(r, out var hit, interactRange))
                {
                    if (hit.collider.gameObject.TryGetComponent(out IDaarkInteractable interactObj))
                    {
                        interactObject = interactObj;
                        interactObj.PickUp(holder.transform);
                    }
                }
            }
        }
    }

}
