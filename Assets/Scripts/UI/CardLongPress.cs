using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems; // ต้องมีตัวนี้ถึงจะจับการแตะหน้าจอได้

public class CardLongPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Header("ตั้งค่าเวลากดค้าง (วินาที)")]
    public float holdDuration = 1.0f; // ค่าเริ่มต้นคือแตะค้าง 1 วินาที

    [Header("ใส่คำสั่งเมื่อกดค้างสำเร็จ")]
    public UnityEvent onLongPress; // ทำให้เราลากฟังก์ชันมาใส่ใน Inspector ได้เหมือนปุ่ม Button ทั่วไป

    private bool isPointerDown = false;
    private float timePressed = 0f;
    private bool isLongPressTriggered = false;

    void Update()
    {
        // ถ้านิ้วยังแตะอยู่ และยังไม่ได้ทริกเกอร์การจอง
        if (isPointerDown && !isLongPressTriggered)
        {
            timePressed += Time.deltaTime; // เริ่มนับเวลา

            // ถ้าเวลาที่กดค้าง มากกว่าหรือเท่ากับ เวลาที่ตั้งไว้
            if (timePressed >= holdDuration)
            {
                isLongPressTriggered = true;
                onLongPress.Invoke(); // เรียกใช้งานคำสั่งที่ผูกไว้
                Debug.Log("จองการ์ดใบนี้!");
            }
        }
    }

    // ทำงานเมื่อ "เริ่มแตะ" หรือ "คลิกเมาส์ค้าง"
    public void OnPointerDown(PointerEventData eventData)
    {
        isPointerDown = true;
        timePressed = 0f;
        isLongPressTriggered = false;
    }

    // ทำงานเมื่อ "ยกนิ้วขึ้น" หรือ "ปล่อยเมาส์"
    public void OnPointerUp(PointerEventData eventData)
    {
        ResetPress();
    }

    // ทำงานเมื่อ "ลากนิ้ว/เมาส์ หลุดออกนอกกรอบของการ์ด" (ป้องกันการจองลั่น)
    public void OnPointerExit(PointerEventData eventData)
    {
        ResetPress();
    }

    private void ResetPress()
    {
        isPointerDown = false;
        timePressed = 0f;
    }
}