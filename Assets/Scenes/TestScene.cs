using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Profiling;

public class TestScene : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
 
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            // Check if the mouse was clicked over a UI element
            if (EventSystem.current.IsPointerOverGameObject())
            {
                Debug.Log("Clicked on the UI");
            }
        }
    }

}
