#!/usr/bin/env bash
# =====================================================================================================
# BITE TEST DRIVER — proves a check can FAIL.
#
#     bash tools/HeadOracle/bite-test.sh <label> <mutation-script>
#
# A check that has never been observed failing is not known to work. This driver takes the engine the
# live arm would have used, applies a DELIBERATE DEFECT to a private third copy under .oracle-work/bite,
# rebuilds it, and runs the real comparator against the pristine head arm.
#
# WHY A THIRD ARM AND NOT A MUTATED src/:
#   src/ and tests/ are never touched, at all, for any reason. A previous agent on this project left a
#   mutation on disk as the final state and reported it as a working fix — the costliest failure this
#   project has had. Keeping every mutation inside .oracle-work/ (git-ignored, wiped on every run) makes
#   that mistake structurally impossible rather than merely forbidden.
#
# PROVENANCE — AUDIT FIX. This driver used to pass the MUTATED tree's real digest into BOTH the
# "live arm" and "working tree" slots. The comparator's equality then held and the report printed
#     "provenance assertion: live arm IS the working tree  => PASS"
# on a document formatted identically to a real verdict, including the "PROVENANCE (ASSERTED, not merely
# printed)" banner. The script header was honest about it; THE REPORT WAS NOT. This project's costliest
# recorded failure was an agent reporting a mutation as the working fix, and a bite report was a
# self-certifying artefact that a mutated engine's provenance had been asserted.
#
# It now passes the literal sentinel BITE-MUTATED for the working-tree slot. The comparator:
#   * stamps "BITE TEST — MUTATED ENGINE — NOT A VERDICT ON THE WORKING TREE" as the report's FIRST line,
#   * makes NO provenance claim at all, and
#   * NEVER exits 0 — a sandbox run exits 4 even when every check passes (a genuine conviction still
#     surfaces as 1 or 3, so a bite test that bites still reads as a bite).
#
# Requires a prior successful `run-oracle.sh` (it reuses .oracle-work/head.tsv and the head arm).
# =====================================================================================================
set -euo pipefail

[ $# -ge 2 ] || { echo "usage: bite-test.sh <label> <mutation-script>" >&2; exit 2; }
LABEL="$1"
MUTATION="$2"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$ROOT"
export PATH="$HOME/.dotnet:$PATH"
unset ApexLedgerProject || true

WORK=".oracle-work"
BITE="$WORK/bite"

[ -f "$WORK/head.tsv" ] || { echo "bite-test: run run-oracle.sh first (no $WORK/head.tsv)." >&2; exit 2; }
[ -d "$WORK/live" ]    || { echo "bite-test: run run-oracle.sh first (no $WORK/live)." >&2; exit 2; }
[ -f "$MUTATION" ]     || { echo "bite-test: mutation script '$MUTATION' not found." >&2; exit 2; }

winpath() {
  if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"
  else echo "$1" | sed 's|^/\([a-zA-Z]\)/|\1:/|'
  fi
}

tree_digest() {
  ( cd "$1" && find . -type f -not -path "*/bin/*" -not -path "*/obj/*" | LC_ALL=C sort \
      | while IFS= read -r f; do
          printf '%s  %s\n' "$(tr -d '\r' < "$f" | sha256sum | cut -d' ' -f1)" "$f"
        done ) | sha256sum | cut -d' ' -f1
}

echo "################################################################################"
echo "# BITE TEST: $LABEL"
echo "# mutation : $MUTATION"
echo "################################################################################"

rm -rf "$BITE"
mkdir -p "$BITE"
cp -R "$WORK/live/src" "$BITE/src"
cp -R "$WORK/live/runner" "$BITE/runner"
rm -rf "$BITE/src/Apex.Ledger/bin" "$BITE/src/Apex.Ledger/obj" \
       "$BITE/runner/bin" "$BITE/runner/obj"

export ENGINE="$ROOT/$BITE/src/Apex.Ledger"
BEFORE="$(tree_digest "$BITE/src/Apex.Ledger")"
bash "$MUTATION" || { echo "bite-test: the mutation script failed." >&2; exit 2; }
AFTER="$(tree_digest "$BITE/src/Apex.Ledger")"
[ "$BEFORE" != "$AFTER" ] || { echo "bite-test: THE MUTATION CHANGED NOTHING ($BEFORE) — a no-op mutation proves nothing." >&2; exit 2; }
echo "mutation applied: engine tree $BEFORE -> $AFTER"

ENGINE_CSPROJ="$(winpath "$ROOT/$BITE/src/Apex.Ledger/Apex.Ledger.csproj")"
if ! dotnet build "$BITE/runner/OracleRunner.csproj" -c Release -v minimal \
      -p:ApexLedgerProject="$ENGINE_CSPROJ" > "$WORK/bite.build.log" 2>&1; then
  tail -30 "$WORK/bite.build.log"
  echo "bite-test: mutated arm failed to BUILD — the mutation is not valid C#." >&2
  exit 2
fi
dotnet "$BITE/runner/bin/Release/net10.0/OracleRunner.dll" emit "$WORK/bite.tsv"

HEAD_TREE="$(tree_digest "$WORK/head/src/Apex.Ledger")"
RC=0
dotnet "$WORK/head/runner/bin/Release/net10.0/OracleRunner.dll" compare \
  "$WORK/head.tsv" "$WORK/bite.tsv" "$WORK/bite-report-$LABEL.txt" \
  "$HEAD_TREE" "$AFTER" "BITE-MUTATED" > "$WORK/bite-stdout-$LABEL.txt" 2>&1 || RC=$?

echo ""
echo "--- BITE RESULT: $LABEL  (comparator exit $RC; 4 = sandbox-clean, 1 = convicted, 3 = harness) ---"
head -4 "$WORK/bite-report-$LABEL.txt"
sed -n '/^  CHECK  1/,/^====/p' "$WORK/bite-report-$LABEL.txt"
if [ "$RC" = "0" ]; then
  echo "bite-test: FATAL — a bite report exited 0. The sandbox sentinel is not being honoured." >&2
  exit 2
fi
echo "full report: $ROOT/$WORK/bite-report-$LABEL.txt"
exit 0
