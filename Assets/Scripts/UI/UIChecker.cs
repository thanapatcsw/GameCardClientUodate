using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem; 
using System.Collections.Generic;

public class UIChecker : MonoBehaviour
{
    void Update()
    {
        if (Mouse.current == null) return;

        // เช็คการคลิกขวา (Right Button)
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            PointerEventData eventData = new PointerEventData(EventSystem.current);
            eventData.position = Mouse.current.position.ReadValue();

            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);

            if (results.Count > 0)
            {
                foreach (var result in results)
                {
                    // พ่นชื่อวัตถุที่เมาส์จิ้มโดนออกมาดู
                    Debug.Log("<color=cyan>[UI Hit]</color> เมาส์จิ้มโดน: " + result.gameObject.name);
                }
            }
        }
    }
}