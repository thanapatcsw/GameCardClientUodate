using System.Collections.Generic;
using UnityEngine;

// ============================================================
// NobleManager — จัดการระบบขุนนาง (Noble) แยกออกจาก GameController
//
//   หน้าที่:
//   • Setup()      สุ่มขุนนาง 4 ใบจาก master pool แล้ววางลง left/right container
//   • CheckClaim() เช็คว่า player คนปัจจุบันมีโบนัสครบเงื่อนไขขุนนางใบไหน
//                  → ให้คะแนน, mark ขุนนางใบนั้นว่าถูก claim, เอาออกจาก active list
//
//   ออกแบบเป็น pure C# class (ไม่ใช่ MonoBehaviour) เพื่อ:
//   • รับ reference จาก GameController ผ่าน constructor — Inspector ไม่ต้องเปลี่ยน
//   • test/แก้ logic ขุนนางได้โดยไม่กระทบส่วนอื่นของเกม (Single Responsibility)
// ============================================================
public class NobleManager
{
    private readonly GameObject noblePrefab;
    private readonly Transform leftContainer;
    private readonly Transform rightContainer;
    private readonly List<NobleData> masterPool;
    private readonly List<NobleDisplay> active = new List<NobleDisplay>();

    public IReadOnlyList<NobleDisplay> Active => active;

    public NobleManager(
        GameObject noblePrefab,
        Transform leftContainer,
        Transform rightContainer,
        List<NobleData> masterPool)
    {
        this.noblePrefab = noblePrefab;
        this.leftContainer = leftContainer;
        this.rightContainer = rightContainer;
        this.masterPool = masterPool;
    }

    /// <summary>สุ่มขุนนาง 4 ใบจาก master pool แล้ววางลงคอนเทนเนอร์ซ้าย/ขวา</summary>
    public void Setup()
    {
        if (masterPool == null || masterPool.Count < 4)
        {
            Debug.LogWarning("[NobleManager] มีขุนนางใน Master น้อยกว่า 4 ใบ! กรุณาใส่ให้ครบก่อน");
            return;
        }

        active.Clear();

        // ก็อปปี้ลิสต์ออกมาสับไพ่ (Fisher-Yates shuffle)
        List<NobleData> tempNobles = new List<NobleData>(masterPool);
        for (int i = 0; i < tempNobles.Count; i++)
        {
            NobleData temp = tempNobles[i];
            int randomIndex = Random.Range(i, tempNobles.Count);
            tempNobles[i] = tempNobles[randomIndex];
            tempNobles[randomIndex] = temp;
        }

        // ดึงมา 4 ใบ — 2 ใบแรกซ้าย, 2 ใบหลังขวา
        for (int i = 0; i < 4; i++)
        {
            NobleData selectedNoble = tempNobles[i];
            Transform targetContainer = (i < 2) ? leftContainer : rightContainer;

            if (targetContainer == null)
            {
                Debug.LogWarning("[NobleManager] ยังไม่ได้ผูก Left/Right Noble Container!");
                continue;
            }

            GameObject nobleObj = Object.Instantiate(noblePrefab, targetContainer);
            NobleDisplay display = nobleObj.GetComponent<NobleDisplay>();

            if (display != null)
            {
                display.SetupNoble(selectedNoble);
                active.Add(display);
            }
        }

        GameLog.Log("[NobleManager] สร้างและสุ่มขุนนาง 4 ใบเรียบร้อย");
    }

    /// <summary>เช็คว่า player มีโบนัสครบเงื่อนไขขุนนางใบไหน → ให้คะแนน + เอาออกจาก active</summary>
    public void CheckClaim(PlayerUI player)
    {
        if (player == null) return;

        // เช็คย้อนกลับ เพราะอาจลบออกจาก active ระหว่าง loop
        for (int i = active.Count - 1; i >= 0; i--)
        {
            NobleDisplay nobleDisplay = active[i];
            NobleData data = nobleDisplay.nobleData;

            bool canClaim = true;
            for (int b = 0; b < 5; b++)
            {
                if (player.bonuses[b] < data.requiredBonuses[b])
                {
                    canClaim = false;
                    break;
                }
            }

            if (canClaim)
            {
                string claimerName = player.nameText != null ? player.nameText.text : "ผู้เล่น";
                GameLog.Log($"[Noble] {claimerName} ได้รับขุนนาง: {data.nobleName} (+{data.victoryPoints} VP)");

                player.AddScore(data.victoryPoints);
                nobleDisplay.ClaimNoble(claimerName);
                active.RemoveAt(i);
            }
        }
    }
}
