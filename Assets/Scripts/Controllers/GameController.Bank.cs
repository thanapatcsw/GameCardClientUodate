using UnityEngine;

// ============================================================
// GameController — ส่วน Resource Bank (เหรียญทรัพยากรในกระดาน)
//
//   จัดการ "กองกลาง" (bank) ของเหรียญทรัพยากร CPU/RAM/Network/Storage/Security/Gold
//   • UpdateBankUI                     : รีเฟรช UI แสดงจำนวนเหรียญแต่ละสีที่เหลือในกองกลาง
//   • ClearPendingCoins                : เคลียร์เหรียญที่ผู้เล่นเลือกค้างไว้ (pending)
//   • Get/SpendCoins helpers           : รวมจำนวน, จ่ายเหรียญ wildcard
//   • OnResourceClicked                : เมื่อผู้เล่นกดปุ่มเหรียญในกระดาน (กฎหยิบ 1-3 สี / 2 สีเดียวกัน)
//   • GetResourceIndex                 : map ชื่อสี → index 0-5
//   • ConfigureBankCoinsByPlayerCount  : ตั้งจำนวนเหรียญตามจำนวนผู้เล่น (2/3/4)
//   • SpawnResourceBank                : สร้าง ResourceButton ทั้ง 6 ปุ่ม
// ============================================================
public partial class GameController
{
    public void UpdateBankUI()
    {
        foreach (ResourceButton btn in bankButtons)
        {
            if (btn != null) {
                int index = GetResourceIndex(btn.resourceType);
                btn.UpdateRemainingUI(bankCoins[index]);
            }
        }
    }

    public void ClearPendingCoins()
    {
        if (BlockActionDuringQuiz()) return;
        if (BlockActionUntilContinue()) return;
        // [FIX] ไม่ block บอท — บอทเรียก ClearPendingCoins ภายในเทิร์นของตัวเอง
        System.Array.Clear(pendingCoins, 0, 6);
        foreach (var btn in bankButtons) if (btn != null) btn.UpdatePendingUI(0);
        ClearWarning();
    }

    int GetTotalPendingCoins()
    {
        int total = 0;
        for (int i = 0; i < 5; i++) total += pendingCoins[i];
        return total;
    }

    int GetTotalPlayerCoins(int playerIndex)
    {
        int total = 0;
        for (int i = 0; i < 6; i++) total += players[playerIndex].coins[i];
        return total;
    }

    int SpendWildcardCoinsWithoutReturningQuizBlack(PlayerUI player, int amount)
    {
        if (player == null || amount <= 0)
        {
            return 0;
        }

        return player.SpendWildcardCoins(amount);
    }

    public void OnResourceClicked(ResourceButton clickedBtn)
    {
        if (BlockActionDuringQuiz()) return;
        if (BlockActionUntilContinue()) return;
        if (BlockActionOutsideLocalTurn()) return;
        if (isGameOver) return;
        if (IsCurrentPlayerBot() && !isExecutingBotTurn) {
            ShowWarning("กำลังเป็นเทิร์นของบอท");
            return;
        }

        int index = GetResourceIndex(clickedBtn.resourceType);

        if (index == 5) {
            ShowWarning("หยิบเหรียญทองโดยตรงไม่ได้! ต้องใช้แอคชั่น 'จองการ์ด' เท่านั้น");
            return;
        }

        if (bankCoins[index] - pendingCoins[index] <= 0) {
            ShowWarning("หยิบไม่ได้! เหรียญสีนี้ในกองกลางหมดแล้ว");
            return;
        }

        int totalPending = GetTotalPendingCoins();
        int totalPlayerCoins = GetTotalPlayerCoins(playOrder[currentPlayerIndex]); // เปลี่ยนเป็นเช็คคนเล่นตามคิว

        if (totalPlayerCoins + totalPending >= 10) {
            ShowWarning("หยิบไม่ได้! คุณถือเหรียญเกิน 10 อันไม่ได้ (ต้องกดซื้อการ์ดเพื่อใช้เหรียญก่อน)");
            return;
        }

        bool hasDoublePick = false;
        for (int i = 0; i < 5; i++) if (pendingCoins[i] >= 2) hasDoublePick = true;

        if (hasDoublePick) {
            ShowWarning("หยิบเพิ่มไม่ได้! คุณเลือกแอคชั่น 'หยิบ 2 เหรียญสีเดียวกัน' ไปแล้ว");
            return;
        }

        if (pendingCoins[index] >= 2) {
            ShowWarning("ผิดกติกา! ไม่สามารถหยิบเหรียญสีเดียวกันเกิน 2 อันได้");
            return;
        }

        if (pendingCoins[index] == 1) {
            if (totalPending > 1) {
                ShowWarning("หยิบซ้ำสีไม่ได้! คุณเริ่มแอคชั่น 'หยิบ 3 สีต่างกัน' ไปแล้ว");
                return;
            }
            if (bankCoins[index] < 4) {
                ShowWarning("หยิบ 2 อันไม่ได้! กองกลางมีเหรียญสีนี้เหลือไม่ถึง 4 อัน");
                return;
            }
            pendingCoins[index]++;
        } else if (pendingCoins[index] == 0) {
            if (totalPending >= 3) {
                ShowWarning("คุณหยิบเหรียญครบ 3 สีแล้ว!");
                return;
            }
            pendingCoins[index]++;
        }

        ClearWarning();
        clickedBtn.UpdatePendingUI(pendingCoins[index]);
    }

    // ───────── Setup / configuration ─────────

    public int GetResourceIndex(string type) {
        if (type == "CPU") return 0; if (type == "RAM") return 1; if (type == "Network") return 2;
        if (type == "Storage") return 3; if (type == "Security") return 4; return 5;
    }

    int GetConfiguredPlayerCount()
    {
        if (isOnlineMatchMode)
        {
            return GetConfiguredOnlinePlayerCount();
        }

        if (players == null || players.Length == 0) return 4;

        int count = 0;
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null) count++;
        }

        return Mathf.Clamp(count, 2, 4);
    }

    void ConfigureBankCoinsByPlayerCount()
    {
        int playerCount = GetConfiguredPlayerCount();
        int coloredCoins = playerCount == 2 ? 4 : playerCount == 3 ? 5 : 7;

        for (int i = 0; i < 5; i++)
        {
            bankCoins[i] = coloredCoins;
        }

        bankCoins[5] = 5;
    }

    void SpawnResourceBank() {
        ClearContainer(resourceBankContainer);
        bankButtons.Clear();
        string[] resNames = { "CPU", "RAM", "Network", "Storage", "Security", "Wildcard" };
        foreach (string res in resNames) {
            GameObject obj = Instantiate(resourcePrefab, resourceBankContainer);
            ResourceButton btn = obj.GetComponent<ResourceButton>();
            if (btn != null) { btn.Setup(this, res); bankButtons.Add(btn); }
        }
    }
}
