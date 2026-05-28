using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class BotController : MonoBehaviour
{
    private GameController gameController;

    // [FIX] cache color→button map แทนการ Find() ทุก call
    // key = resource index (0-4), value = ResourceButton
    private Dictionary<int, ResourceButton> _colorToButtonCache;

    void Awake()
    {
        gameController = GetComponent<GameController>();
    }

    // สร้าง/รีเฟรช cache ถ้า bankButtons เปลี่ยน
    private Dictionary<int, ResourceButton> GetColorButtonMap()
    {
        if (_colorToButtonCache != null && _colorToButtonCache.Count > 0)
            return _colorToButtonCache;

        _colorToButtonCache = new Dictionary<int, ResourceButton>();
        if (gameController?.bankButtons == null) return _colorToButtonCache;

        foreach (var btn in gameController.bankButtons)
        {
            if (btn == null) continue;
            int idx = gameController.GetResourceIndex(btn.resourceType);
            if (idx >= 0 && idx < 5 && !_colorToButtonCache.ContainsKey(idx))
                _colorToButtonCache[idx] = btn;
        }
        return _colorToButtonCache;
    }

    public void ExecuteTurn(int playerIndex)
    {
        if (gameController == null) return;

        PlayerUI botPlayer = gameController.players[playerIndex];
        // [FIX] null-safe nameText + รีเฟรช cache ถ้ายังไม่มี
        _colorToButtonCache = null; // invalidate cache ทุก turn เผื่อ bankButtons เปลี่ยน
        string botName = botPlayer?.nameText != null ? botPlayer.nameText.text : $"Bot {playerIndex + 1}";
        GameLog.Log($"<color=orange>[Bot] เริ่มเทิร์นของบอท: {botName}</color>");

        bool actionTaken = false;
        // [FIX] ครอบ try/finally รับประกันว่าถ้าเกิด Exception หรือทุก action ล้มเหลว
        // เกมจะยัง EndTurn ให้เสมอ ไม่ค้างถาวร
        try
        {
            // --- Priority 1: Check Win Condition ---
            CardDisplay winningCard = FindWinningCard(botPlayer);
            if (winningCard != null)
            {
                GameLog.Log($"<color=green>[Bot] พบโอกาสชนะ! ซื้อการ์ด ID: {winningCard.data.cardId}</color>");
                PerformBuyAction(winningCard);
                actionTaken = true;
                return;
            }

            // --- Priority 2: Buy Best Affordable Card ---
            CardDisplay bestCard = FindBestAffordableCard(botPlayer);
            if (bestCard != null)
            {
                GameLog.Log($"<color=green>[Bot] ตัดสินใจซื้อการ์ดที่ดีที่สุด ID: {bestCard.data.cardId}</color>");
                PerformBuyAction(bestCard);
                actionTaken = true;
                return;
            }

            // --- Priority 3: Reserve Important Tier 3 ---
            // [FIX] เช็ค pendingReserveCard หลัง Prompt เพื่อยืนยันว่า action ไม่โดนบล็อก
            CardDisplay reserveTarget = FindReserveTarget(botPlayer);
            if (reserveTarget != null)
            {
                GameLog.Log($"<color=yellow>[Bot] ตัดสินใจจองการ์ด Tier 3 ID: {reserveTarget.data.cardId}</color>");
                gameController.PromptReserveCard(reserveTarget);
                // ถ้า PromptReserveCard ถูกบล็อก pendingReserveCard จะเป็น null → ไม่ ConfirmReserve
                if (gameController.pendingReserveCard != null)
                {
                    gameController.ConfirmReserve(); // บอทกดยืนยันทันที
                    actionTaken = true;
                    return;
                }
                GameLog.Log("[Bot] Reserve ถูกบล็อก → ข้ามไป Priority 4");
            }

            // --- Priority 4: Collect Resources ---
            CollectBestResources(botPlayer);
            actionTaken = true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Bot] ExecuteTurn เกิด Exception: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            // [FIX] Safety net: ถ้าทุก action ล้มเหลวหรือเกิด Exception โดยไม่มี EndTurn
            // ให้บังคับ EndTurn เพื่อไม่ให้เกมค้าง
            if (!actionTaken)
            {
                GameLog.Log("[Bot] Safety net: ไม่มี action ใดสำเร็จ → บังคับ EndTurn");
                gameController.EndTurn();
            }
        }
    }

    private CardDisplay FindWinningCard(PlayerUI player)
    {
        var allCards = GetAllCardsOnBoard().Concat(GetReservedCards(player));
        foreach (var card in allCards)
        {
            if (CanAfford(player, card.data) && (player.currentScore + card.data.victoryPoints >= gameController.winningScore))
            {
                return card;
            }
        }
        return null;
    }

    private CardDisplay FindBestAffordableCard(PlayerUI player)
    {
        var affordableCards = GetAllCardsOnBoard().Concat(GetReservedCards(player))
            .Where(c => CanAfford(player, c.data))
            .OrderByDescending(c => c.data.victoryPoints)
            .ThenBy(c => GetTotalCost(c.data))
            .ToList();

        return affordableCards.FirstOrDefault();
    }

    private CardDisplay FindReserveTarget(PlayerUI player)
    {
        if (player.reservedCards.Count >= 3) return null;

        // มองหา Tier 3 ที่ขาดทรัพยากรแค่ 1-2 เหรียญ
        var tier3Cards = GetCardsInContainer(gameController.tier3Container);
        foreach (var card in tier3Cards)
        {
            int missing = GetMissingCoinsCount(player, card.data);
            if (missing > 0 && missing <= 2) return card;
        }
        return null;
    }

    private void CollectBestResources(PlayerUI player)
    {
        // คำนวณว่าสีไหนที่บอทต้องการมากที่สุด (จากการ์ดที่มีคะแนนสูงสุดบนบอร์ด)
        int[] needs = GetMostWantedResourceColors(player);
        List<int> colorsToPick = new List<int>();

        // กฎการหยิบเหรียญ: 
        // [FIX] ใช้ cache แทน Find() ทุก call
        var colorMap = GetColorButtonMap();

        // [FIX] คำนวณ capacity ที่เหลือก่อนตัดสินใจหยิบ 2 อัน
        int currentTotalCoins = 0;
        for (int ci = 0; ci < 6; ci++) currentTotalCoins += player.coins[ci];
        int coinCapacityLeft = 10 - currentTotalCoins;

        // 1. ถ้ามีสีที่ขาดเยอะและกองกลางมี >= 4 -> หยิบ 2 อัน (ถ้ามี capacity เพียงพอ)
        if (coinCapacityLeft >= 2)
        {
            for (int i = 0; i < 5; i++)
            {
                int colorIdx = needs[i];
                if (gameController.bankCoins[colorIdx] >= 4)
                {
                    GameLog.Log($"<color=cyan>[Bot] หยิบเหรียญสี {colorIdx} จำนวน 2 อัน</color>");
                    if (colorMap.TryGetValue(colorIdx, out ResourceButton btn))
                    {
                        gameController.OnResourceClicked(btn);
                        gameController.OnResourceClicked(btn);
                    }
                    gameController.EndTurn();
                    return;
                }
            }
        }

        // 2. ถ้าหยิบ 2 ไม่ได้ -> หยิบ 3 สีที่ต้องการที่สุด (ตามที่ capacity เหลือ)
        int maxPick = Mathf.Min(3, coinCapacityLeft);
        int pickedCount = 0;
        for (int i = 0; i < 5 && pickedCount < maxPick; i++)
        {
            int colorIdx = needs[i];
            if (gameController.bankCoins[colorIdx] > 0 && colorMap.TryGetValue(colorIdx, out ResourceButton btn))
            {
                gameController.OnResourceClicked(btn);
                pickedCount++;
            }
        }

        if (pickedCount > 0)
        {
            GameLog.Log($"<color=cyan>[Bot] หยิบเหรียญต่างกัน {pickedCount} สี</color>");
            gameController.EndTurn();
        }
        else
        {
            // ถ้าหยิบไม่ได้เลย (กองกลางว่างหมด) -> ต้องจำใจจองการ์ดซักใบเพื่อเอาทอง (ถ้ามีทอง) หรือข้ามเทิร์น
            GameLog.Log("[Bot] ไม่มีเหรียญให้หยิบ! พยายามจองการ์ดแทน");
            CardDisplay anyCard = GetAllCardsOnBoard().FirstOrDefault();
            if (anyCard != null && player.reservedCards.Count < 3)
            {
                gameController.PromptReserveCard(anyCard);
                gameController.ConfirmReserve();
            }
            else
            {
                GameLog.Log("[Bot] ทำอะไรไม่ได้เลย -> ข้ามเทิร์น");
                gameController.EndTurn();
            }
        }
    }

    private void PerformBuyAction(CardDisplay card)
    {
        if (card.isReserved)
            gameController.BuyReservedCard(card);
        else
            gameController.OnCardClicked(card);
    }

    // --- Helpers ---

    private bool CanAfford(PlayerUI player, CardData card)
    {
        int missingCoins = 0;
        for (int i = 0; i < 5; i++)
        {
            int actualCost = Mathf.Max(0, card.costs[i] - player.bonuses[i]);
            if (player.coins[i] < actualCost)
                missingCoins += (actualCost - player.coins[i]);
        }
        return missingCoins <= player.coins[5]; // check against Gold (Wildcard)
    }

    private int GetMissingCoinsCount(PlayerUI player, CardData card)
    {
        int missing = 0;
        for (int i = 0; i < 5; i++)
        {
            int actualCost = Mathf.Max(0, card.costs[i] - player.bonuses[i]);
            if (player.coins[i] < actualCost)
                missing += (actualCost - player.coins[i]);
        }
        // ลบด้วยจำนวนทองที่มีอยู่
        missing -= player.coins[5];
        return Mathf.Max(0, missing);
    }

    private int GetTotalCost(CardData card)
    {
        return card.costs.Sum();
    }

    private List<CardDisplay> GetAllCardsOnBoard()
    {
        List<CardDisplay> cards = new List<CardDisplay>();
        cards.AddRange(GetCardsInContainer(gameController.tier1Container));
        cards.AddRange(GetCardsInContainer(gameController.tier2Container));
        cards.AddRange(GetCardsInContainer(gameController.tier3Container));
        return cards;
    }

    private List<CardDisplay> GetCardsInContainer(Transform container)
    {
        List<CardDisplay> list = new List<CardDisplay>();
        if (container == null) return list;
        foreach (Transform t in container)
        {
            CardDisplay d = t.GetComponent<CardDisplay>();
            if (d != null && d.data != null) list.Add(d);
        }
        return list;
    }

    private List<CardDisplay> GetReservedCards(PlayerUI player)
    {
        List<CardDisplay> list = new List<CardDisplay>();
        if (player.reservedAreaTransform == null) return list;
        foreach (Transform t in player.reservedAreaTransform)
        {
            CardDisplay d = t.GetComponent<CardDisplay>();
            if (d != null && d.data != null) list.Add(d);
        }
        return list;
    }

    private int[] GetMostWantedResourceColors(PlayerUI player)
    {
        // คำนวณความต้องการทรัพยากรโดยรวมจากการ์ดทั้งหมดบนบอร์ด
        // ให้คะแนนตาม Victory Points ของการ์ดใบนั้นๆ
        float[] scores = new float[5];
        var allCards = GetAllCardsOnBoard();

        foreach (var card in allCards)
        {
            float weight = card.data.victoryPoints + 1.0f; // การ์ดมีแต้มสำคัญกว่า
            for (int i = 0; i < 5; i++)
            {
                int needed = Mathf.Max(0, card.data.costs[i] - player.bonuses[i] - player.coins[i]);
                scores[i] += needed * weight;
            }
        }

        // คืนค่า index ของสีที่ต้องการมากที่สุดไปหาน้อยที่สุด
        return Enumerable.Range(0, 5).OrderByDescending(i => scores[i]).ToArray();
    }
}
