#!/usr/bin/env bash
# =====================================================================================================
# HEAD-ORACLE HARNESS — THE single entry point. Run it from anywhere:
#
#     bash tools/HeadOracle/run-oracle.sh
#
# What it does, in order:
#   0. UNSETS $ApexLedgerProject and passes it EXPLICITLY on every build  (audit C1)
#   1. verifies .oracle-baseline/manifest.sha256                          -> HARD FAIL on mismatch
#   2. verifies the baseline FILE COUNT and SORTED PATH LIST              -> HARD FAIL  (audit H5)
#   3. verifies .oracle-baseline/commit.txt against the pinned commit     -> HARD FAIL
#   4. wipes and rebuilds .oracle-work/head and .oracle-work/live         -> checked    (audit L1)
#   5. head arm <- .oracle-baseline ; live arm <- src/ AT RUN TIME, never cached
#      then NORMALISES LINE ENDINGS TO LF in both arms                    (audit M1)
#   6. computes a WHOLE-TREE digest of each arm (per-file hash + sorted path list) and ASSERTS
#      head arm == pristine baseline and live arm == working src/         -> HARD FAIL  (audit H4/H5)
#   7. builds and runs each arm -> head.tsv, live.tsv
#   8. compares -> report.txt, and exits with the comparator's verdict
#
# EXIT CODES
#   0  clean            — harness sound AND the engine under test accepted
#   1  ENGINE REJECTED  — the harness is sound and it convicts the engine
#   2  harness could not run (IO, build, provenance, usage)
#   3  HARNESS BROKEN   — the oracle cannot be trusted; judge nothing until it is fixed
#
# This script runs NO git at all (R4: git is A12's exclusive domain). The pristine arm is proved
# pristine by the recorded hash manifest + path list, not by a git command. NOTE HONESTLY: the pin
# proves the SNAPSHOT is intact; it does not prove the commit it came from was innocent.
#
# It never builds inside src/ or tests/ and never builds at the repo root — all build output lands in
# .oracle-work/, which is git-ignored.
# =====================================================================================================
set -euo pipefail

# The commit .oracle-baseline/ was materialised from.
ORACLE_BASELINE_COMMIT="9c2bded"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$ROOT" || { echo "ORACLE FAIL: cannot cd to worktree root $ROOT" >&2; exit 2; }

# The .NET SDK is not on PATH in this environment.
export PATH="$HOME/.dotnet:$PATH"

# ----------------------------------------------------------------- 0. AUDIT C1 — kill the env var
# $ApexLedgerProject in the environment silently redirected BOTH arms to a foreign engine while the
# report still printed a pristine hash and said CLEAN. Command-line properties beat the environment,
# so the variable is unset here AND passed explicitly on every build below.
if [ -n "${ApexLedgerProject:-}" ]; then
  echo "NOTE: ApexLedgerProject was set in the environment ('${ApexLedgerProject}') — unsetting it."
fi
unset ApexLedgerProject || true

BASELINE=".oracle-baseline"
WORK=".oracle-work"
ENGINE_REL="Services/StockValuationService.cs"

fail()  { echo ""; echo "ORACLE FAIL: $*" >&2; exit 2; }
broken(){ echo ""; echo "ORACLE HARNESS BROKEN: $*" >&2; exit 3; }
step()  { echo ""; echo "=== $* ==="; }

# An absolute path MSBuild will accept on this platform.
winpath() {
  if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"
  else echo "$1" | sed 's|^/\([a-zA-Z]\)/|\1:/|'
  fi
}

# The whole-tree digest of an engine directory: for every non-build file, the SHA-256 of its
# LF-NORMALISED bytes, paired with its path, sorted, then hashed. Hashing the PATH LIST too is what
# lets an ADDED file be detected (audit H5) — and the negative-stock fix is likely to arrive as one.
tree_digest() {
  local root="$1"
  ( cd "$root" && find . -type f -not -path "*/bin/*" -not -path "*/obj/*" | LC_ALL=C sort \
      | while IFS= read -r f; do
          printf '%s  %s\n' "$(tr -d '\r' < "$f" | sha256sum | cut -d' ' -f1)" "$f"
        done ) | sha256sum | cut -d' ' -f1
}

tree_count() {
  ( cd "$1" && find . -type f -not -path "*/bin/*" -not -path "*/obj/*" | wc -l | tr -d ' ' )
}

# ----------------------------------------------------------------- 1. baseline manifest
step "1. verifying baseline manifest (CWD = $ROOT)"
mkdir -p "$WORK" || fail "cannot create $WORK"
MANIFEST_LOG="$WORK/manifest-verify.log"
[ -f "$BASELINE/manifest.sha256" ] || fail "$BASELINE/manifest.sha256 is missing — A12 must materialise the baseline first."
command -v sha256sum >/dev/null 2>&1 || fail "sha256sum not found on PATH."
if ! sha256sum -c "$BASELINE/manifest.sha256" > "$MANIFEST_LOG" 2>&1; then
  echo "--- manifest verification output (mismatches) ---"
  grep -v ': OK$' "$MANIFEST_LOG" | head -40 || true
  broken "baseline manifest verification FAILED — the pristine HEAD arm has drifted."
fi
MANIFEST_OK=$(grep -c ': OK$' "$MANIFEST_LOG" || true)
echo "manifest OK: $MANIFEST_OK files verified against $BASELINE/manifest.sha256"

# ----------------------------------------------------------------- 2. AUDIT H5 — count + path list
step "2. verifying baseline file COUNT and SORTED PATH LIST (a manifest cannot see an ADDED file)"
MANIFEST_PATHS="$WORK/manifest-paths.txt"
ACTUAL_PATHS="$WORK/baseline-paths.txt"
sed 's/^[0-9a-f]\{64\} \*\{0,1\}//' "$BASELINE/manifest.sha256" | LC_ALL=C sort > "$MANIFEST_PATHS"
find "$BASELINE/src" -type f | LC_ALL=C sort > "$ACTUAL_PATHS"
MANIFEST_N=$(wc -l < "$MANIFEST_PATHS" | tr -d ' ')
ACTUAL_N=$(wc -l < "$ACTUAL_PATHS" | tr -d ' ')
if [ "$MANIFEST_N" != "$ACTUAL_N" ]; then
  broken "baseline file count mismatch: manifest lists $MANIFEST_N files, $BASELINE/src holds $ACTUAL_N."
fi
if ! diff -u "$MANIFEST_PATHS" "$ACTUAL_PATHS" > "$WORK/baseline-pathdiff.log" 2>&1; then
  head -30 "$WORK/baseline-pathdiff.log" || true
  broken "baseline PATH LIST differs from the manifest — a file was added, removed or renamed."
fi
echo "path list OK: $ACTUAL_N files, manifest and on-disk tree agree exactly"

# ----------------------------------------------------------------- 3. baseline commit pin
step "3. verifying baseline commit pin"
[ -f "$BASELINE/commit.txt" ] || fail "$BASELINE/commit.txt is missing — cannot prove which commit the snapshot came from."
RECORDED_COMMIT="$(tr -d '[:space:]' < "$BASELINE/commit.txt")"
[ -n "$RECORDED_COMMIT" ] || fail "$BASELINE/commit.txt is empty."
[ "$RECORDED_COMMIT" = "$ORACLE_BASELINE_COMMIT" ] || \
  broken "baseline commit mismatch: commit.txt says '$RECORDED_COMMIT', harness is configured for '$ORACLE_BASELINE_COMMIT'."
echo "commit pin OK: baseline snapshot is from $RECORDED_COMMIT"
echo "(honest caveat: the pin proves the SNAPSHOT is intact, not that the commit was innocent)"

# ----------------------------------------------------------------- 4. wipe + rebuild the work tree
step "4. wiping $WORK/head and $WORK/live"
rm -rf "$WORK/head" "$WORK/live" || fail "could not wipe the work arms."
[ ! -d "$WORK/head" ] || fail "$WORK/head still exists after the wipe."
[ ! -d "$WORK/live" ] || fail "$WORK/live still exists after the wipe."
mkdir -p "$WORK/head" "$WORK/live" || fail "could not recreate the work arms."
echo "wiped and recreated."

# ----------------------------------------------------------------- 5. materialise both engine arms
copy_engine() { # $1 = source engine dir, $2 = destination engine dir
  local src="$1" dst="$2"
  [ -d "$src" ] || fail "engine source directory '$src' does not exist."
  mkdir -p "$dst" || fail "cannot create '$dst'."
  cp -R "$src/." "$dst/" || fail "copy '$src' -> '$dst' failed."
  find "$dst" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + 2>/dev/null || true
  [ -f "$dst/$ENGINE_REL" ] || fail "'$dst/$ENGINE_REL' missing after copy."
  # AUDIT M1 — normalise line endings so the two arms are comparable at all. `git archive` writes LF
  # while the working tree carries CRLF on some files, which made the v1 provenance claim false on
  # day zero (4 of 291 files differed for no reason but line endings).
  find "$dst" -type f -name '*.cs' -exec sed -i 's/\r$//' {} + || fail "LF normalisation failed in '$dst'."
}

step "5. materialising engine arms (live is re-copied from src/ every run — never cached)"
copy_engine "$BASELINE/src/Apex.Ledger" "$WORK/head/src/Apex.Ledger"
copy_engine "src/Apex.Ledger"           "$WORK/live/src/Apex.Ledger"
echo "head arm <- $BASELINE/src/Apex.Ledger  ($(tree_count "$WORK/head/src/Apex.Ledger") files)"
echo "live arm <- src/Apex.Ledger           ($(tree_count "$WORK/live/src/Apex.Ledger") files)  [at run time]"

step "5b. installing a private runner into each arm"
for arm in head live; do
  mkdir -p "$WORK/$arm/runner" || fail "cannot create the $arm runner directory."
  cp "$SCRIPT_DIR"/*.cs "$WORK/$arm/runner/" || fail "could not copy runner sources into the $arm arm."
  cp "$SCRIPT_DIR/OracleRunner.csproj" "$WORK/$arm/runner/" || fail "could not copy the csproj into the $arm arm."
  echo "  $arm: runner installed at $WORK/$arm/runner"
done

# ----------------------------------------------------------------- 6. AUDIT H4 — ASSERT provenance
step "6. asserting provenance (the three hashes are COMPARED, not merely printed)"
HEAD_TREE="$(tree_digest "$WORK/head/src/Apex.Ledger")"
LIVE_TREE="$(tree_digest "$WORK/live/src/Apex.Ledger")"
BASE_TREE="$(tree_digest "$BASELINE/src/Apex.Ledger")"
WORK_TREE="$(tree_digest "src/Apex.Ledger")"

echo "  baseline tree digest : $BASE_TREE"
echo "  head arm  digest     : $HEAD_TREE"
echo "  working src/ digest  : $WORK_TREE"
echo "  live arm  digest     : $LIVE_TREE"

[ "$HEAD_TREE" = "$BASE_TREE" ] || \
  broken "head arm is NOT the pristine baseline tree ($HEAD_TREE != $BASE_TREE)."
[ "$LIVE_TREE" = "$WORK_TREE" ] || \
  broken "live arm is NOT the working src/Apex.Ledger tree ($LIVE_TREE != $WORK_TREE)."
echo "  provenance ASSERTED: head arm == pristine baseline, live arm == working src/"

HEAD_ENGINE_SHA="$(tr -d '\r' < "$WORK/head/src/Apex.Ledger/$ENGINE_REL" | sha256sum | cut -d' ' -f1)"
LIVE_ENGINE_SHA="$(tr -d '\r' < "$WORK/live/src/Apex.Ledger/$ENGINE_REL" | sha256sum | cut -d' ' -f1)"
echo "  StockValuationService.cs (LF-normalised): head=$HEAD_ENGINE_SHA"
echo "                                            live=$LIVE_ENGINE_SHA"

# ----------------------------------------------------------------- 7. build + run each arm
step "7. building and running each arm"
for arm in head live; do
  ENGINE_CSPROJ="$(winpath "$ROOT/$WORK/$arm/src/Apex.Ledger/Apex.Ledger.csproj")"
  echo "--- $arm: build (ApexLedgerProject=$ENGINE_CSPROJ) ---"
  if ! dotnet build "$WORK/$arm/runner/OracleRunner.csproj" -c Release -v minimal \
        -p:ApexLedgerProject="$ENGINE_CSPROJ" > "$WORK/$arm.build.log" 2>&1; then
    tail -40 "$WORK/$arm.build.log" || true
    fail "$arm arm failed to build."
  fi
  grep -E "Warning\(s\)|Error\(s\)" "$WORK/$arm.build.log" | sed "s/^/  $arm: /" || true

  # Prove the build really referenced THIS arm's engine and nothing else.
  if ! grep -qF "$WORK/$arm/src/Apex.Ledger" <(tr '\\' '/' < "$WORK/$arm.build.log"); then
    echo "--- build log ---"; cat "$WORK/$arm.build.log"
    broken "$arm arm's build log does not mention $WORK/$arm/src/Apex.Ledger — wrong engine was compiled."
  fi

  DLL="$WORK/$arm/runner/bin/Release/net10.0/OracleRunner.dll"
  [ -f "$DLL" ] || fail "$arm arm produced no $DLL."
  echo "--- $arm: emit ---"
  dotnet "$DLL" emit "$WORK/$arm.tsv" || fail "$arm arm failed while emitting $WORK/$arm.tsv."
done

# ----------------------------------------------------------------- 8. compare
step "8. comparing"
# The comparison is always driven by the HEAD arm's runner: the pristine side decides the verdict.
RC=0
dotnet "$WORK/head/runner/bin/Release/net10.0/OracleRunner.dll" compare \
  "$WORK/head.tsv" "$WORK/live.tsv" "$WORK/report.txt" \
  "$HEAD_TREE" "$LIVE_TREE" "$WORK_TREE" || RC=$?

echo ""
case "$RC" in
  0) echo "ORACLE: CLEAN (exit 0). Report: $ROOT/$WORK/report.txt" ;;
  1) echo "ORACLE: ENGINE REJECTED (exit 1). Report: $ROOT/$WORK/report.txt" >&2 ;;
  3) echo "ORACLE: HARNESS BROKEN (exit 3) — judge nothing. Report: $ROOT/$WORK/report.txt" >&2 ;;
  *) echo "ORACLE: comparator failed (exit $RC). Report: $ROOT/$WORK/report.txt" >&2 ;;
esac
exit "$RC"
