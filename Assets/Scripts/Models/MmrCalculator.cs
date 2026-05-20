/// <summary>
/// คำนวณ MMR ตามผลจบเกม — ฟีล DOTA2
/// placement: 1 = อันดับ 1 (ชนะ), totalPlayers: จำนวนผู้เล่นทั้งหมด
/// </summary>
public static class MmrCalculator
{
    public static int Calculate(int placement, int totalPlayers)
    {
        if (totalPlayers == 2)
            return placement == 1 ? +25 : -25;

        if (totalPlayers == 3)
        {
            if (placement == 1) return +25;
            if (placement == 2) return -5;
            return -20;
        }

        if (placement == 1) return +30;
        if (placement == 2) return +10;
        if (placement == 3) return -10;
        return -25;
    }

    public static string GetRankName(int mmr)
    {
        if (mmr >= 3000) return "Legend";
        if (mmr >= 2500) return "Diamond";
        if (mmr >= 2000) return "Platinum";
        if (mmr >= 1500) return "Gold";
        if (mmr >= 1000) return "Silver";
        return "Bronze";
    }

    public static int Clamp(int mmr) => UnityEngine.Mathf.Max(0, mmr);
}
