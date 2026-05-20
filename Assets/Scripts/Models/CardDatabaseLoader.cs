using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// โหลดข้อมูลการ์ดทั้งหมดจาก cards_database.json ครั้งเดียว
/// แบ่ง tier1 / tier2 / tier3 ให้ GameController ใช้งาน
/// </summary>
public static class CardDatabaseLoader
{
    private static bool isLoaded = false;
    private static List<CardData> allCards = new List<CardData>();
    private static List<CardData> tier1 = new List<CardData>();
    private static List<CardData> tier2 = new List<CardData>();
    private static List<CardData> tier3 = new List<CardData>();

    public static List<CardData> AllCards  { get { EnsureLoaded(); return allCards; } }
    public static List<CardData> Tier1Cards { get { EnsureLoaded(); return tier1; } }
    public static List<CardData> Tier2Cards { get { EnsureLoaded(); return tier2; } }
    public static List<CardData> Tier3Cards { get { EnsureLoaded(); return tier3; } }

    /// <summary>
    /// เรียกเพื่อโหลดข้อมูล (ถ้ายังไม่ได้โหลด)
    /// </summary>
    public static void EnsureLoaded()
    {
        if (isLoaded) return;
        LoadFromJson();
    }

    /// <summary>
    /// บังคับโหลดใหม่ (ถ้าเปลี่ยน JSON ระหว่าง runtime)
    /// </summary>
    public static void ForceReload()
    {
        isLoaded = false;
        allCards.Clear();
        tier1.Clear();
        tier2.Clear();
        tier3.Clear();
        LoadFromJson();
    }

    private static void LoadFromJson()
    {
        TextAsset jsonFile = Resources.Load<TextAsset>("cards_database");
        if (jsonFile == null)
        {
            Debug.LogError("[CardDatabaseLoader] ไม่พบไฟล์ Resources/cards_database.json!");
            isLoaded = true;
            return;
        }

        CardDatabaseJson db = JsonUtility.FromJson<CardDatabaseJson>(jsonFile.text);
        if (db == null || db.cards == null)
        {
            Debug.LogError("[CardDatabaseLoader] JSON parse ผิดพลาด!");
            isLoaded = true;
            return;
        }

        foreach (var entry in db.cards)
        {
            // สร้าง ScriptableObject ใน memory (ไม่ต้องมีไฟล์ .asset)
            CardData card = ScriptableObject.CreateInstance<CardData>();
            card.cardId       = entry.cardId;
            card.cardName     = entry.cardName;
            card.category     = entry.category;
            card.tier         = entry.tier;
            card.victoryPoints = entry.victoryPoints;
            card.bonusType    = entry.bonusType;
            card.imageName    = entry.imageName;
            card.name         = entry.cardId; // ตั้งชื่อ SO ให้ debug ง่าย

            // แปลง costs
            card.costs = new int[5];
            if (entry.costs != null)
            {
                card.costs[0] = entry.costs.cpu;
                card.costs[1] = entry.costs.ram;
                card.costs[2] = entry.costs.network;
                card.costs[3] = entry.costs.storage;
                card.costs[4] = entry.costs.security;
            }

            allCards.Add(card);

            switch (entry.tier)
            {
                case 1: tier1.Add(card); break;
                case 2: tier2.Add(card); break;
                case 3: tier3.Add(card); break;
            }
        }

        isLoaded = true;
        Debug.Log($"[CardDatabaseLoader] โหลดการ์ดสำเร็จ! รวม {allCards.Count} ใบ (T1:{tier1.Count} T2:{tier2.Count} T3:{tier3.Count})");
    }

    // ---- JSON mapping classes ----

    [System.Serializable]
    private class CardDatabaseJson
    {
        public CardEntryJson[] cards;
    }

    [System.Serializable]
    private class CardEntryJson
    {
        public string cardId;
        public string cardName;
        public string category;
        public int tier;
        public int victoryPoints;
        public int bonusType;
        public string imageName;
        public CostsJson costs;
    }

    [System.Serializable]
    private class CostsJson
    {
        public int cpu;
        public int ram;
        public int network;
        public int storage;
        public int security;
    }
}
