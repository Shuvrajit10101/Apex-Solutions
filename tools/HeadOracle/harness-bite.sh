#!/usr/bin/env bash
# =====================================================================================================
# HARNESS BITE TEST — proves the HARNESS-INTEGRITY assertions (PART A) can fail.
#
#     bash tools/HeadOracle/harness-bite.sh <label> <mutation-script>
#
# bite-test.sh mutates the ENGINE and asks "does the check convict a bad engine?".
# This one mutates the HARNESS ITSELF and asks "does the harness notice when IT is the broken thing?".
# That question matters more: an engine defect costs a rework cycle, a silently-broken oracle ships the
# defect. Every mutation lives under .oracle-work/ (git-ignored); src/ and tests/ are never touched.
#
# $RUNNER is exported to the mutation script and points at the mutated runner sources.
# The mutated runner both EMITS and COMPARES, so a poisoned reference or a check that quietly stopped
# evaluating anything is judged by the very code that contains the fault — exactly the situation the
# integrity assertions exist for.
# =====================================================================================================
set -euo pipefail

[ $# -ge 2 ] || { echo "usage: harness-bite.sh <label> <mutation-script>" >&2; exit 2; }
LABEL="$1"
MUTATION="$2"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$ROOT"
export PATH="$HOME/.dotnet:$PATH"
unset ApexLedgerProject || true

WORK=".oracle-work"
HB="$WORK/hbite"

[ -d "$WORK/head" ] || { echo "harness-bite: run run-oracle.sh first." >&2; exit 2; }
[ -f "$MUTATION" ]  || { echo "harness-bite: mutation script '$MUTATION' not found." >&2; exit 2; }

winpath() {
  if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"
  else echo "$1" | sed 's|^/\([a-zA-Z]\)/|\1:/|'
  fi
}

echo "################################################################################"
echo "# HARNESS BITE TEST: $LABEL"
echo "# mutation : $MUTATION   (mutates the HARNESS, not the engine)"
echo "################################################################################"

rm -rf "$HB"
mkdir -p "$HB"
cp -R "$WORK/head/src" "$HB/src"
cp -R "$WORK/head/runner" "$HB/runner"
rm -rf "$HB/src/Apex.Ledger/bin" "$HB/src/Apex.Ledger/obj" "$HB/runner/bin" "$HB/runner/obj"

export RUNNER="$ROOT/$HB/runner"
BEFORE="$(cat "$HB"/runner/*.cs | sha256sum | cut -d' ' -f1)"
bash "$MUTATION" || { echo "harness-bite: the mutation script failed." >&2; exit 2; }
AFTER="$(cat "$HB"/runner/*.cs | sha256sum | cut -d' ' -f1)"
[ "$BEFORE" != "$AFTER" ] || { echo "harness-bite: THE MUTATION CHANGED NOTHING — proves nothing." >&2; exit 2; }
echo "harness mutated: runner sources $BEFORE -> $AFTER"

ENGINE_CSPROJ="$(winpath "$ROOT/$HB/src/Apex.Ledger/Apex.Ledger.csproj")"
if ! dotnet build "$HB/runner/OracleRunner.csproj" -c Release -v minimal \
      -p:ApexLedgerProject="$ENGINE_CSPROJ" > "$WORK/hbite.build.log" 2>&1; then
  tail -30 "$WORK/hbite.build.log"
  echo "harness-bite: mutated harness failed to BUILD." >&2
  exit 2
fi

DLL="$HB/runner/bin/Release/net10.0/OracleRunner.dll"
EMIT_RC=0
dotnet "$DLL" emit "$WORK/hbite.tsv" || EMIT_RC=$?
[ "$EMIT_RC" = "0" ] || echo "harness-bite: emit exited $EMIT_RC (that may itself be the bite)"

# Compare the mutated harness's own output against itself: the ENGINE is identical on both sides, so
# every engine check is trivially clean and ONLY the harness-integrity assertions can speak.
RC=0
dotnet "$DLL" compare "$WORK/hbite.tsv" "$WORK/hbite.tsv" "$WORK/hbite-report-$LABEL.txt" \
  SAME SAME BITE-MUTATED > "$WORK/hbite-stdout-$LABEL.txt" 2>&1 || RC=$?

echo ""
echo "--- HARNESS BITE RESULT: $LABEL  (comparator exit $RC; 3 = HARNESS BROKEN, 4 = sandbox-clean) ---"
if [ -s "$WORK/hbite-report-$LABEL.txt" ]; then
  head -4 "$WORK/hbite-report-$LABEL.txt"
  sed -n '/^PART A/,/^PART B/p' "$WORK/hbite-report-$LABEL.txt"
  sed -n '/^HARNESS INTEGRITY/,/^ENGINE VERDICT/p' "$WORK/hbite-report-$LABEL.txt"
else
  echo "NO REPORT WAS WRITTEN — the comparator refused to run at all. Its output:"
  tail -20 "$WORK/hbite-stdout-$LABEL.txt"
fi
if [ "$RC" = "0" ]; then
  echo "harness-bite: FATAL — a harness-bite report exited 0. The mutation was not caught." >&2
  exit 2
fi
echo "full report: $ROOT/$WORK/hbite-report-$LABEL.txt"
exit 0
