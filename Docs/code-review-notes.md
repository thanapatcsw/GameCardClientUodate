# Code Review Notes (2026-06-17)

รีวิวโค้ดส่วน gameplay / networking / security / state — บันทึกไว้เพื่อตามแก้

---

## 🔴 ควรปรับจริง (เชิงสถาปัตยกรรม)

### 1. Online sync = "last-writer-wins" → desync (เหรียญ/การ์ดหาย)
**ไฟล์:** `GameController.Network.cs` — `ApplyEconomySnapshot`, `ApplyBoardSnapshot`

ทุก client broadcast **state เต็มก้อน** (economy + board) แล้วฝั่งรับ **เขียนทับดิบๆ** ไม่มี version/authority คุมว่า snapshot ไหนใหม่กว่า
- อาการ: client A หยิบเหรียญ → snapshot เก่าจาก B (build ก่อน A หยิบ) ลอยมาทีหลัง → ทับเหรียญ A หาย
- = ตรงกับบั๊ก **"หยิบเหรียญแล้วไม่ได้เหรียญใน online"** ที่เจอ
- board sync ก็โมเดลเดียวกัน (RebuildTierIfChanged กันกระพริบได้ แต่ไม่กัน revert)

**วิธีแก้:** ✅ **ทำแล้ว (2026-06-19) — version guard** ที่ economy + board snapshot
- ติด `Version = totalTurnCount` (logical clock ที่ sync อยู่แล้ว) ไปกับ snapshot (FusionManager struct + payload ต่อท้าย backward-compatible)
- ฝั่งรับ (`HandleOnlineEconomyStateReceived`/`BoardStateReceived`) **ข้าม snapshot ที่ `Version < _lastApplied`** → snapshot เก่า revert ของใหม่ไม่ได้
- ปลอดภัย: topology ส่งครั้งเดียว/เครื่อง (ไม่ duplicate) → ไม่มี false-skip; ถ้า parse version พลาด → degrade เป็นพฤติกรรมเดิม
- มี log `[NetDiag] APPLY-ECON/BOARD SKIP stale ...` ให้เห็นตอนกันได้จริง
**สถานะ:** ต้องเทสต์ 2 เครื่องยืนยัน (ผมเทสต์ multiplayer เองไม่ได้). แก้สนิทระยะยาว = host-authority เต็ม (parked)

---

## 🟡 ข้อจำกัดที่ควรรู้ (ยังไม่ต้องแก้)

1. **GameLogger flush ตอน `OnApplicationQuit`** = fire-and-forget async → ปิดแอปดิบๆ HTTP อาจส่งไม่ทัน (log ก้อนท้ายตก). จบเกมปกติ (FlushNow ใน CheckWinCondition) ปลอดภัย เพราะแอปยังรัน — best-effort ยอมรับได้
2. ~~**`[NetDiag]` logs** spam ทุก publish/apply~~ → ✅ **ถอดออกหมดแล้ว (2026-07-09)** (เหลือ log disconnect ที่ยิงครั้งเดียวตอนหลุด)
3. ~~**submit-match-result: client โกหกอันดับ + ฟาร์มรางวัลได้**~~ → ✅ **แก้แล้ว (2026-07-09)**: เพิ่ม membership gate เช็ค `matchmaking_queue.player_id = auth.uid()` ต่อ room → คน create-room เองแล้วยิงผลไม่มีแถวในคิว = ถูกปฏิเสธ (ฟาร์มไม่ได้). **ยังเหลือ:** ในแมตช์ ranked จริง client ยัง "โกหกอันดับตัวเอง" (placement=1) ได้ 1 ครั้ง/แมตช์ → แก้สนิทต้อง host-authority ส่งผลของทุกคน (parked). ผลข้างเคียงที่ตั้งใจ: custom room ที่ไม่ผ่าน matchmaking = unranked

---

## 🟢 จุดเล็ก (optional cleanup)

1. **client ควิซรายวันยังส่ง `p_user_id`/`p_reward_gems`** ไป RPC ที่ตอนนี้ ignore แล้ว (`PlayerDataService` SubmitDailyQuizAnswer/HasClaimed/FetchUnanswered) — ลบออกให้สะอาดได้ ไม่อันตราย
2. **`SubmitMatchResultAsync` ประกอบ JSON ด้วย string interpolation** (roomCode/roomId) — ปลอดภัยเพราะค่าเป็น alphanumeric แต่ใช้ serializer จะเป๊ะกว่า
3. **GameLogger.Enqueue อ่าน `PlayerPrefs.GetString` ทุกครั้ง** — ถูกมาก cache ได้

---

## 🔧 รอบแก้ 2026-07-09 (full-system review)

- ✅ **OTP HTML injection** (`send-otp`): `username` (client คุมได้) ถูกยัดลง htmlContent ดิบๆ → escape + จำกัด 40 ตัว (กันฟิชชิ่งผ่าน sender ที่เชื่อถือ)
- ✅ **gems race (read-modify-write)** ใน `purchase-item` / `grant-quiz-reward` / `submit-match-result`: เปลี่ยนเป็น optimistic guard (`.eq(gems, oldGems)` + retry 5x) → กัน "ได้ของ 2 จ่ายเงินอันเดียว" ตอนยิงพร้อมกัน
- ✅ **seat reclaim ถูก `Sort()` ทำเพี้ยน** (`FusionManager.RefreshSeatOrder`): Sort เฉพาะก่อนมี uid binding (`_uidSeat.Count==0`) → reclaim ไม่ถูกสลับเมื่อมีผู้เล่นใหม่ join หลัง reconnect
- ✅ **dead code**: ลบ `ReconnectManager.PendingBoardSnapshot` (write-only)
- ℹ️ **version guard within-turn** (`_lastAppliedEcon/BoardVersion`, guard `<`): ตรวจแล้วปลอดภัยด้วย invariant "1 publisher/เทิร์น" (acting player publish ตอนเทิร์นตัวเอง, host publish ตอน quiz) + reliable delivery รักษาลำดับ → **ไม่ต้องแก้โค้ด** เหลือแค่ยืนยันด้วยเทสต์ 2 เครื่อง
- ⏸️ **email enumeration** (`send-otp` คืน 409 "อีเมลถูกใช้แล้ว"): คงไว้ตั้งใจ — เป็น UX จำเป็นของหน้าสมัคร (ถ้าเงียบผู้ใช้จะงงว่าทำไมสมัคร/ยืนยันไม่ได้); ความเสี่ยงต่ำมาก

### รอบเจาะลึก 2 (gameplay/online correctness)
- ✅ **turn timer ยิงทุกเครื่อง** (`GameController.Update` → `ForceEndTurn`): เดิมทุก client นับเวลาแล้วพอหมดต่างคนต่าง `ForceEndTurn` → เสี่ยงเทิร์นเลื่อนซ้อน/ข้าม 2 seat + noble/turnCount เพี้ยน (การ broadcast reset timer ช่วยได้บางเคสแต่ race อยู่). แก้: online ให้ **authority คนเดียวขับ timeout**, non-authority ค้าง timer รอ turn-state broadcast
- ✅ **question index ไม่ตรงข้ามเครื่อง**: `SendQuizStart(index)` ส่งแค่ index ในคลัง แต่คลัง (`get_active_questions` RPC ไม่การันตี ORDER, หรือ cache/JSON คนละแหล่ง) อาจเรียงต่างกัน → คนละเครื่องเห็นคนละข้อ. แก้: `SortQuestionDatabaseDeterministically()` (เรียงด้วย id) หลังโหลดทั้ง 3 ทาง (Supabase/cache/JSON)
- ✔️ **NobleManager.CheckClaim เก็บขุนนางได้หลายใบใน 1 เทิร์น** (Splendor แท้ = 1 ใบ/เทิร์น) — **user ยืนยัน 2026-07-09 ว่าคงไว้ตั้งใจ** (ดีไซน์เกมนี้ให้เก็บได้หลายใบ) → ไม่แก้
- ✅ ตรวจแล้วโอเค: ซื้อ/จองการ์ด (coins[5] รวม black แล้ว → affordability ถูก), quiz reward (host apply + broadcast econ, client `applyRewardsToState:false` ไม่ double), noble sync (version-guarded + ไม่บวกซ้ำ), bot (online ไม่รัน), matchmaking pref wiring (ยืนยัน #1 gate ผ่านเกม ranked จริง)

---

## ✅ ตรวจแล้วโอเค (ไม่มีปัญหา)

- **กฎหยิบเหรียญ** (`GameController.Bank.cs` OnResourceClicked) — 1-3 สี / 2 สีเดียว, ลิมิต 10, กองพอ — ตรงกับ Game.Core ✓
- **`MmrCalculator.cs` == สูตรใน edge function** (25/-25, 25/-5/-20, 30/10/-10/-25, Clamp max 0) — client/server ตรงกัน ✓
- **`PlayerUI.cs`** — `SpendWildcardCoins` (จ่าย black ก่อน คืนทองจริง) + `AddQuizBlackCoin` ตรงกับ Game.Core ✓
- **บอท** (`GameController.Bots.cs`) — รันเฉพาะ authority (online), เช็ค authority สดหลัง delay, กันทุกเครื่องรันชนกัน ✓
- **`QuizManager` timeout (#3)** — แก้แล้ว (~5 วิ → fallback cache/JSON) ✓
- **GameLogger buffer/flush** — guard `_flushing` กัน overlap, MaxBufferGuard กันบวม, ไม่ล็อกอิน→ข้าม, main-thread ล้วน ✓
- **Backend security** — ทุกตารางเปิด RLS, RPC ใช้ auth.uid() หมด, gem/mmr grant คุมฝั่ง server, daily-quiz RPC + submit-match-result hardened แล้ว ✓
- **action hooks (Log→DB)** — null-guard + capture ค่าก่อน Destroy/Clear ทุกจุด ✓
