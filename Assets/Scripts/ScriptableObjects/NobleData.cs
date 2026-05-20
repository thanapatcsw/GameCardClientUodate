using UnityEngine;

[CreateAssetMenu(fileName = "NewNoble", menuName = "GameCard/NobleData")]
public class NobleData : ScriptableObject
{
    [Header("ชื่อหมวดหมู่หรือชื่อขุนนาง")]
    public string nobleName;

    [Header("รูปภาพขุนนาง")]
    public Sprite artwork;

    [Header("คะแนนสะสมที่ได้รับเมื่อครอบครอง")]
    public int victoryPoints = 3;

    [Header("เงื่อนไขการ์ดสะสม (Bonuses) ที่ต้องการ")]
    [Tooltip("ใส่จำนวนสัญลักษณ์การ์ดที่ต้องการ (เรียงตามลำดับแร่)")]
    public int[] requiredBonuses = new int[5]; 
    // ตัวอย่าง: ถ้าต้องการ CPU 3 ใบ, RAM 3 ใบ, Network 3 ใบ -> ใส่ [3,3,3,0,0]
}
