# THE CENSUS GATE — "a check that quietly evaluated much LESS must fail too".
#
# The most realistic wrong-fix shape there is: just add the guard, and refuse the voucher at posting
# time. Company.AddInventoryVoucher throws on any Outward allocation, so Corpus.Build throws for every
# guard-bypass (G*/E1) scenario, Emit `continue`s, and all of their rows are simply ABSENT from the live
# arm. The point oracle iterates the live arm's keys, so absent rows are neither evaluated NOR counted
# as mismatches.
#
# BEFORE: 'CHECK 3 point oracle (FIFO/LIFO) : PASS' with 'subjects evaluated : 134' against 332.
# CHECK 5 PASS, CHECK 9 PASS, CHECK 10 PASS. Checks 6/7/8 printed 'live 0/0' for E1 and every single G
# family and STILL printed PASS, because the assertion only fired when the WHOLE-ARM sum was zero.
# Only CHECK 2 and CHECK 11 rejected the run; a narrower change (throwing for Fifo/Lifo only) would
# have left one check between this and a CLEAN verdict.
# AFTER: the CENSUS GATE compares the live arm's evaluated count to the head arm's, cell by cell, and
# raises a HARNESS failure (exit 3 — the oracle has lost coverage, so judge nothing). ROW-SET SYMMETRY
# independently reports every key present on head and missing on live.
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"
python "$HERE/_patch.py" "$ENGINE/Domain/Company.cs" \
'    public void AddInventoryVoucher(InventoryVoucher voucher) => _inventoryVouchers.Add(voucher ?? throw new ArgumentNullException(nameof(voucher)));' \
'    public void AddInventoryVoucher(InventoryVoucher voucher)
    {
        ArgumentNullException.ThrowIfNull(voucher);
        // CENSUS BITE: "just add the guard" — refuse every outward at posting time.
        foreach (var __a in voucher.Allocations)
            if (__a.Direction == StockDirection.Outward)
                throw new InvalidOperationException("CENSUS BITE: outward refused at posting time.");
        _inventoryVouchers.Add(voucher);
    }'
