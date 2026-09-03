# TallyPrime valuation test — 7 books, ~20 minutes

**Purpose.** Settle how TallyPrime actually values stock, so our engine copies a measured rule instead of
an invented one. Eight previous attempts invented a model; every one produced an unbounded
Balance-Sheet error. Each cell below becomes a golden constant in the test harness.

**Use a legitimate TallyPrime — Educational Mode is free from tallysolutions.com and is sufficient.**
Not the Tally 7.2 in Downloads: wrong product, and twenty years out of date.

---

## Setup (once)

1. Create a company — any name. **F11 → Integrate Accounts with Inventory = Yes.** (Without this the
   Balance Sheet ignores inventory entirely and half the test tells you nothing.)
2. **F11 → Maintain Multiple Godowns / Locations = Yes.**
3. Create godowns **G1** and **G2**.
4. Create a stock item per test — **Costing Method = Avg. Cost** unless a test says otherwise.
5. Educational Mode only allows the **1st, 2nd and 31st** of a month. Use **1-May**, **2-May**, **31-May**.

**After each step record, from Stock Summary (with godown breakup, `F7` Show Profit if offered):**
closing **quantity**, closing **rate**, closing **value** — per godown *and* the item total. Then the
**Balance Sheet Stock-in-Hand** figure.

---

## ▶ T3 — RUN THIS FIRST. One number decides everything.

| # | Date | Entry | Godown |
|---|---|---|---|
| 1 | 1-May | Purchase **10 @ ₹100** | G1 |
| 2 | 2-May | Sales **5** (any sale price) | G1 |
| 3 | 31-May | Purchase **5 @ ₹200** | G1 |

**Read: closing value on 31-May.** Closing quantity will be 10.

- **₹1,333.33** → Tally uses a cumulative inward pool (2,000 ÷ 15 = 133.33). Our engine is wrong and the
  Average Cost rewrite is justified. Continue to T1.
- **₹1,500.00** → Tally uses a perpetual moving average, the same as our engine. **The central claim is
  wrong, stop here, and tell me — nothing else should be built.**

---

## T1 — does a negative quantity carry a negative value?

*The actual negative-stock question, and the one no documentation answers.*

| # | Date | Entry | Godown |
|---|---|---|---|
| 1 | 1-May | Purchase **10 @ ₹100** | G1 |
| 2 | 2-May | Sales **15** | G1 |

Tally will warn about negative stock — **accept and save anyway.** Closing quantity: **−5**.

**Read: closing value and Balance-Sheet Stock-in-Hand.** Predicted **−₹500**.
Record whichever it is: a negative value, zero, or something else.

## T2 — recovery (continue in the same item, do not delete T1)

| # | Date | Entry | Godown |
|---|---|---|---|
| 3 | 31-May | Purchase **15 @ ₹200** | G1 |

Closing quantity: **+10**. Predicted **₹1,600** (pool 4,000 ÷ 25 = 160).

*This is the case eight attempts got wrong, each differently. Please record the rate as well as the value.*

## T4 — per godown, or per item?

Fresh item.

| # | Date | Entry | Godown |
|---|---|---|---|
| 1 | 1-May | Purchase **10 @ ₹100** | **G1** |
| 2 | 2-May | Sales **5** | **G2** ← the empty one |

**Read: value for G1, for G2, and the item total.**

- Total **₹1,000** (G1 1,000, G2 0) → per-godown valuation.
- Total **₹500** → item-level valuation.

*This settles the question that stopped the work eight times.*

## T5 — is the pool as-of dated, or the whole year?

Fresh item.

| # | Date | Entry | Godown |
|---|---|---|---|
| 1 | 1-May | Purchase **10 @ ₹100** | G1 |
| 2 | 31-May | Purchase **1 @ ₹1,000,000** | G1 |

**Now view Stock Summary as at 2-May** (change the period end date to 2-May).

- **₹1,000** → the pool only counts movements up to the report date.
- **~₹910,000** → the pool is annual, and a *later* purchase reprices an *earlier* Balance Sheet.

## T6 — does a transfer carry its cost?

Fresh item.

| # | Date | Entry | Godown |
|---|---|---|---|
| 1 | 1-May | Purchase **10 @ ₹0.37** | G1 |
| 2 | 2-May | **Stock Journal**: transfer **4** G1 → G2, **leave the destination rate blank** | G1→G2 |
| 3 | 31-May | Purchase **1 @ ₹1,000,000.03** | G1 |

**Read: value for G1, G2, and total.** Predicted total **₹1,000,003.73** — exactly what was spent.

*If the total exceeds what was spent, the transfer is re-pricing units, which is the defect that broke
our own attempt at per-godown valuation.*

## T7 — what rate values a count in a godown with no purchases?

Fresh item, **set a Standard Cost of ₹9.77** on the item master.

| # | Date | Entry | Godown |
|---|---|---|---|
| 1 | 1-May | Purchase **5 @ ₹100.13** | G1 |
| 2 | 2-May | **Physical Stock** voucher, count **30** | **G2** (never purchased into) |

**Read: value for G2 and the total.**

- G2 **₹0.00** (total ₹500.65) → an empty godown's pool rate is zero; the standard cost is not used.
- G2 **₹293.10** (30 × 9.77) → the standard cost fills in.

---

## Reporting back

For each test just give me the numbers you see — quantity, rate, value per godown and total, plus
Stock-in-Hand. **If a figure surprises you, that is the most valuable thing in this exercise**: it means
our prediction was wrong, which is exactly what this test exists to find out before code is written.

If Tally refuses an entry, or shows something the table doesn't anticipate, tell me what it did rather
than working around it — the refusal is itself a fact about the model.

---

# Part 2 — four more books, ~15 minutes

Added 2026-08-05. These do not test valuation; each one **unblocks a fix that cannot be written
without the answer**. Same company, same 1st/2nd/31st date rule. Run them after T1–T7.

## T8 — is the interest rate per PERIOD or per ANNUM?

Create a party ledger with **Activate Interest Calculation = Yes**, then in its interest parameters set
**Rate = 10%** and **Per = 30-Day Month**. Put a **₹44,000** balance on it and view the Interest
report over a **30-day** window.

- **₹4,400** → the rate is **per period** (10% of the balance, for one 30-day month).
- **~₹366** → the rate is **per annum** (10%/year, apportioned to 30 days).

*Unblocks:* our divisors are all annualised. Note this fix is needed under **either** answer — a separate
Calendar-Month ×12 error is wrong both ways — but the correct formula depends on this number.

## T9 — how is a rateless inward costed?

Fresh item, **Costing Method = FIFO**.

| # | Date | Entry |
|---|---|---|
| 1 | 1-May | Purchase **10 @ ₹100** |
| 2 | 2-May | **Stock Journal** — destination line **in**, quantity **5**, **rate left blank** |

**Read: closing quantity, rate and value.** Predicted 15 units; the question is what the 5 unrated units
carry — ₹100 each (the running rate), ₹0, or something else.

*Unblocks:* our "best-available-cost chain" (running average → StandardCost → last rated inward → 0) is
**our own invention** — no Tally source describes it. This tells us what to replace it with.

## T10 — does TDS catch up on earlier below-threshold bills?

Needs TDS enabled, a **§194J** nature (threshold ₹50,000, rate 10%) and a party with PAN.

| # | Date | Entry |
|---|---|---|
| 1 | 1-May | Professional fees **₹20,000** |
| 2 | 2-May | Professional fees **₹20,000** |
| 3 | 31-May | Professional fees **₹20,000** ← crosses ₹60,000 cumulative |

**Read: the TDS deducted on the THIRD bill.**

- **₹2,000** → deducts on that bill only (₹20,000 × 10%).
- **₹6,000** → catches up on the whole ₹60,000.
- **₹1,000** → deducts only on the ₹10,000 excess over the threshold.

*Unblocks:* our engine only ever charges the current transaction, and separately computes **§194Q on the
whole purchase value instead of the excess** — while TCS §206C(1H) right beside it uses the correct
excess rule. This settles which model each section follows.

## T11 — does a Manual voucher number accept letters?

Set any voucher type to **Method of Voucher Numbering = Manual**, open a voucher of that type, and type
**`M/15/7`** into Voucher No.

**Read: does it save, or is the field numeric-only?**

*Unblocks:* our `Voucher.Number` is an `int` and the entry field is read-only, so a Tally book using
alphanumeric manual numbers cannot be represented at all. If Tally accepts it, that is a data-model
change and needs planning rather than patching.

---

## Two more, only if you have the appetite

**T12 — back-dated insert.** Enter three vouchers dated 1, 2 and 31 May under Automatic numbering, then a
fourth dated 2-May. **Do the existing numbers change?** (Settles whether "Renumber" re-sequences on
insert or only on delete.)

**T13 — the 2-digit year pivot.** Type **`31/12/30`** into any date field and read back the year Tally
renders — **1930** or **2030**. (Our parser uses .NET's 1930–2029 pivot, which is wrong on any pivot an
Indian accounting package would plausibly choose.)
