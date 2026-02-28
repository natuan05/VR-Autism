using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Spin : MonoBehaviour
{
    private Animator animator;
    public float Intro = 3.0f;
    // Start is called before the first frame update
    void Start()
    {
        animator = GetComponent<Animator>();
        if (animator != null )
        {
            StartCoroutine(Trigger());
        }
    }

    private IEnumerator Trigger()
    {
        yield return new WaitForSeconds(Intro);
        animator.SetTrigger("TrSpin");
    }
  
}
