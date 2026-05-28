using System.Collections.Generic;
using UnityEngine;

// ============================================================
// GameController — ส่วน Card interaction & board setup
//   • OnCardClicked, BuyReservedCard       → ซื้อการ์ด (board / reserved)
//   • PromptReserveCard, ConfirmReserve,
//     CancelReserve, ExecuteReserve        → flow การจองการ์ด
//   • PopulateBoard, DrawNewCard           → แจกการ์ดลงกระดาน Tier 1/2/3
//   • LoadCardDatabase                     → โหลด CardData จาก JSON
//   • ClearContainer                       → util ใช้ทั้ง board spawn และ network resync
// ============================================================
public partial class GameController
{
    public void OnCardClicked(CardDisplay card)
    {
        if (BlockActionDuringQuiz()) return;
        if (BlockActionUntilContinue()) return;
        if (BlockActionOutsideLocalTurn()) return;
        if (isGameOver) return;
        if (IsCurrentPlayerBot() && !isExecutingBotTurn) {
            ShowWarning("กำลังเป็นเทิร์นของบอท");
            return;
        }

        if (GetTotalPendingCoins() > 0) {
            ShowWarning("คุณกำลังทำ 2 แอคชั่น! กรุณากดปุ่ม Clear เหรียญออกก่อนกดซื้อการ์ด");
            return;
        }

        PlayerUI p = players[playOrder[currentPlayerIndex]]; // เปลี่ยนเป็นเช็คคนเล่นตามคิว
        int missingCoins = 0;

        for (int i = 0; i < 5; i++) {
            int actualCost = Mathf.Max(0, card.data.costs[i] - p.bonuses[i]);
            if (p.coins[i] < actualCost) {
                missingCoins += (actualCost - p.coins[i]);
            }
        }

        bool canAfford = (missingCoins <= p.coins[5]);

        if (canAfford) {
            for (int i = 0; i < 5; i++) {
                int actualCost = Mathf.Max(0, card.data.costs[i] - p.bonuses[i]);
                if (p.coins[i] < actualCost) {
                    int diff = actualCost - p.coins[i];
                    bankCoins[i] += p.coins[i];
                    p.coins[i] = 0;

                    int goldCoinsReturned = SpendWildcardCoinsWithoutReturningQuizBlack(p, diff);
                    bankCoins[5] += goldCoinsReturned;
                } else {
                    p.coins[i] -= actualCost;
                    bankCoins[i] += actualCost;
                }
            }

            p.AddScore(card.data.victoryPoints);
            p.AddBonus(card.data.bonusType);
            p.UpdateUI();

            Transform parentContainer = card.transform.parent;
            int tier = (parentContainer == tier3Container) ? 3 : (parentContainer == tier2Container) ? 2 : 1;
            int slotIndex = card.transform.GetSiblingIndex(); // จำช่องเดิมไว้ก่อนดึงการ์ดออก
            // ดึงออกจาก container ก่อน Destroy (deferred) ไม่งั้น BuildBoardSnapshot จะนับใบที่กำลังถูกลบติดไปด้วย
            card.transform.SetParent(null);
            Destroy(card.gameObject);
            DrawNewCard(tier, parentContainer, slotIndex);

            ClearWarning();
            UpdateBankUI();
            EndTurn();
        } else {
            ShowWarning("ซื้อการ์ดไม่ได้! เหรียญของคุณไม่พอ (รวมส่วนลดและทองแล้ว)");
        }
    }

    public void PromptReserveCard(CardDisplay card)
    {
        if (BlockActionDuringQuiz()) return;
        if (BlockActionUntilContinue()) return;
        if (BlockActionOutsideLocalTurn()) return;
        if (isGameOver) return;
        if (IsCurrentPlayerBot() && !isExecutingBotTurn) {
            ShowWarning("กำลังเป็นเทิร์นของบอท");
            return;
        }
        if (GetTotalPendingCoins() > 0) {
            ShowWarning("ทำ 2 แอคชั่นไม่ได้! กรุณา Clear เหรียญก่อนจองการ์ด");
            return;
        }

        PlayerUI p = players[playOrder[currentPlayerIndex]]; // เปลี่ยนเป็นเช็คคนเล่นตามคิว
        if (p.reservedCards.Count >= 3) {
            ShowWarning("จองเพิ่มไม่ได้! คุณมีการ์ดจองในมือเต็ม 3 ใบแล้ว");
            return;
        }

        pendingReserveCard = card;
        if (confirmReservePanel != null) {
            confirmReservePanel.SetActive(true);
        }
    }

    public void ConfirmReserve()
    {
        if (BlockActionDuringQuiz()) return;
        if (BlockActionUntilContinue()) return;
        if (BlockActionOutsideLocalTurn()) return;
        if (IsCurrentPlayerBot() && !isExecutingBotTurn) {
            ShowWarning("กำลังเป็นเทิร์นของบอท");
            return;
        }
        if (confirmReservePanel != null) confirmReservePanel.SetActive(false);
        if (pendingReserveCard != null) {
            ExecuteReserve(pendingReserveCard);
            pendingReserveCard = null;
        }
    }

    public void CancelReserve()
    {
        if (BlockActionDuringQuiz()) return;
        if (BlockActionUntilContinue()) return;
        if (BlockActionOutsideLocalTurn()) return;
        if (IsCurrentPlayerBot() && !isExecutingBotTurn) {
            ShowWarning("กำลังเป็นเทิร์นของบอท");
            return;
        }
        if (confirmReservePanel != null) confirmReservePanel.SetActive(false);
        pendingReserveCard = null;
    }

    private void ExecuteReserve(CardDisplay card)
    {
        PlayerUI p = players[playOrder[currentPlayerIndex]]; // เปลี่ยนเป็นเช็คคนเล่นตามคิว
        p.reservedCards.Add(card.data);

        int goldIndex = 5;
        int totalPlayerCoins = GetTotalPlayerCoins(playOrder[currentPlayerIndex]); // เปลี่ยนเป็นเช็คคนเล่นตามคิว
        if (bankCoins[goldIndex] > 0 && totalPlayerCoins < 10) {
            bankCoins[goldIndex]--; p.coins[goldIndex]++; p.UpdateUI();
        } else if (bankCoins[goldIndex] <= 0) {
            ShowWarning("จองสำเร็จ! แต่ไม่ได้เหรียญทอง (กองกลางหมด)");
        } else if (totalPlayerCoins >= 10) {
            ShowWarning("จองสำเร็จ! แต่ไม่ได้เหรียญทอง (คุณถือเหรียญเต็ม 10 อันแล้ว)");
        }

        if (p.reservedAreaTransform != null) {
            GameObject resCard = Instantiate(cardPrefab, p.reservedAreaTransform);
            resCard.transform.localScale = new Vector3(0.6f, 0.6f, 1f);

            CardDisplay resDisplay = resCard.GetComponent<CardDisplay>();
            resDisplay.LoadCardData(card.data);
            resDisplay.isReserved = true;
            resDisplay.ownerUI = p;
        }

        Transform parentContainer = card.transform.parent;
        int tier = (parentContainer == tier3Container) ? 3 : (parentContainer == tier2Container) ? 2 : 1;
        int slotIndex = card.transform.GetSiblingIndex(); // จำช่องเดิมไว้ก่อนดึงการ์ดออก
        // ดึงออกจาก container ก่อน Destroy (deferred) ไม่งั้น BuildBoardSnapshot จะนับใบที่กำลังถูกลบติดไปด้วย
        card.transform.SetParent(null);
        Destroy(card.gameObject);
        DrawNewCard(tier, parentContainer, slotIndex);

        ClearWarning();
        UpdateBankUI();
        EndTurn();
    }

    public void BuyReservedCard(CardDisplay card)
    {
        if (BlockActionDuringQuiz()) return;
        if (BlockActionUntilContinue()) return;
        if (BlockActionOutsideLocalTurn()) return;
        if (isGameOver) return;

        PlayerUI p = players[playOrder[currentPlayerIndex]]; // เปลี่ยนเป็นเช็คคนเล่นตามคิว

        if (IsCurrentPlayerBot() && !isExecutingBotTurn) {
            ShowWarning("กำลังเป็นเทิร์นของบอท");
            return;
        }

        if (card.ownerUI != p) {
            ShowWarning("ไม่สามารถซื้อการ์ดจองของผู้เล่นอื่นได้!");
            return;
        }

        if (GetTotalPendingCoins() > 0) {
            ShowWarning("กรุณา Clear เหรียญก่อนกดซื้อการ์ด!");
            return;
        }

        int missingCoins = 0;
        for (int i = 0; i < 5; i++) {
            int actualCost = Mathf.Max(0, card.data.costs[i] - p.bonuses[i]);
            if (p.coins[i] < actualCost) {
                missingCoins += (actualCost - p.coins[i]);
            }
        }

        bool canAfford = (missingCoins <= p.coins[5]);

        if (canAfford) {
            for (int i = 0; i < 5; i++) {
                int actualCost = Mathf.Max(0, card.data.costs[i] - p.bonuses[i]);
                if (p.coins[i] < actualCost) {
                    int diff = actualCost - p.coins[i];
                    bankCoins[i] += p.coins[i]; p.coins[i] = 0;
                    int goldCoinsReturned = SpendWildcardCoinsWithoutReturningQuizBlack(p, diff);
                    bankCoins[5] += goldCoinsReturned;
                } else {
                    p.coins[i] -= actualCost; bankCoins[i] += actualCost;
                }
            }

            p.AddScore(card.data.victoryPoints);
            p.AddBonus(card.data.bonusType);
            p.reservedCards.Remove(card.data);
            p.UpdateUI();

            Destroy(card.gameObject);

            ClearWarning();
            UpdateBankUI();
            EndTurn();
        } else {
            ShowWarning("การ์ดที่คุณจองไว้ยังไม่สามารถซื้อได้ เพราะเหรียญไม่พอ!");
        }
    }

    // ───────── Board setup ─────────

    void PopulateBoard()
    {
        ClearContainer(tier3Container);
        ClearContainer(tier2Container);
        ClearContainer(tier1Container);
        for (int i = 0; i < 4; i++) {
            DrawNewCard(3, tier3Container);
            DrawNewCard(2, tier2Container);
            DrawNewCard(1, tier1Container);
        }
    }

    void DrawNewCard(int tier, Transform container, int slotIndex = -1)
    {
        List<CardData> masterDeck = tier == 3 ? tier3Cards : tier == 2 ? tier2Cards : tier1Cards;
        if (masterDeck == null || masterDeck.Count == 0) return;

        // สร้าง list การ์ดที่ยังไม่เคยถูกใช้
        List<CardData> availableCards = new List<CardData>();
        foreach (var card in masterDeck)
        {
            if (!usedCardIds.Contains(card.cardId)) availableCards.Add(card);
        }

        // ถ้าหมดกองแล้ว ไม่สุ่มใบใหม่ขึ้นมา
        if (availableCards.Count == 0)
        {
            GameLog.Log($"[GameController] กอง Tier {tier} หมดแล้ว ไม่มีการ์ดให้สุ่มเพิ่ม");
            return;
        }

        CardData selectedCard = availableCards[Random.Range(0, availableCards.Count)];
        usedCardIds.Add(selectedCard.cardId); // บันทึกว่าใบนี้ถูกใช้แล้ว
        GameObject newCardObj = Instantiate(cardPrefab, container);
        newCardObj.GetComponent<CardDisplay>()?.LoadCardData(selectedCard);

        // วางการ์ดใหม่ที่ช่องเดิมของใบที่หายไป (ถ้าระบุมา) แทนการต่อท้าย
        if (slotIndex >= 0)
        {
            newCardObj.transform.SetSiblingIndex(slotIndex);
        }
    }

    /// <summary>โหลดข้อมูลการ์ดจาก cards_database.json อัตโนมัติ</summary>
    void LoadCardDatabase()
    {
        CardDatabaseLoader.EnsureLoaded();
        tier1Cards = CardDatabaseLoader.Tier1Cards;
        tier2Cards = CardDatabaseLoader.Tier2Cards;
        tier3Cards = CardDatabaseLoader.Tier3Cards;
        GameLog.Log($"[GameController] โหลดการ์ดจาก JSON สำเร็จ! T1:{tier1Cards.Count} T2:{tier2Cards.Count} T3:{tier3Cards.Count}");
    }

    // ───────── Container util (used by board spawn + network resync) ─────────

    void ClearContainer(Transform c) {
        if (c == null) return;
        // เก็บลูกทั้งหมดก่อน แล้วค่อย detach+Destroy เพื่อไม่ให้ใบที่กำลังถูกลบ (deferred)
        // ยังถูกนับว่าอยู่ใน container ตอน BuildBoardSnapshot/rebuild ในเฟรมเดียวกัน
        List<Transform> children = new List<Transform>();
        foreach (Transform child in c) children.Add(child);
        foreach (Transform child in children) {
            child.SetParent(null);
            Destroy(child.gameObject);
        }
    }
}
