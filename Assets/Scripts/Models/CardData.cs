using UnityEngine;

/// <summary>
/// ข้อมูลการ์ด 1 ใบ — สร้างจาก CardDatabaseLoader (JSON) อัตโนมัติ
/// costs index: 0=CPU, 1=RAM, 2=Network, 3=Storage, 4=Security
/// bonusType:   0=CPU, 1=RAM, 2=Network, 3=Storage, 4=Security
/// </summary>
public class CardData : ScriptableObject
{
    public string cardId;       // เช่น "cpu_transistor"
    public string cardName;     // เช่น "Transistor"
    public string category;     // เช่น "CPU"
    public int tier;

    [Header("Costs (CPU, RAM, Network, Storage, Security)")]
    public int[] costs = new int[5];

    public int victoryPoints;

    [Header("Card Bonus (ส่วนลดที่ให้)")]
    [Tooltip("0:CPU, 1:RAM, 2:Network, 3:Storage, 4:Security")]
    public int bonusType;

    public string imageName;    // ชื่อรูป (ไม่ต้องมี .png)
}