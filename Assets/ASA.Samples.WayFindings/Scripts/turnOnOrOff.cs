using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class turnOnOrOff : MonoBehaviour
{
    //define the 
    public GameObject ObjectToBeHide = null;

    // Start is called before the first frame update
    void Start()
    {
        //ObjectToBeHide = this.gameObject;
    }

    // Update is called once per frame
    public void show()
    {

        if (ObjectToBeHide.activeSelf == false)
        {
            Debug.Log("show again");
            ObjectToBeHide.SetActive(true);
        }

    }

    public void hide()
    {
        if (ObjectToBeHide.activeSelf == true)
        {
            ObjectToBeHide.SetActive(false);
        }
    }
}
