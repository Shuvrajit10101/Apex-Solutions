"""Exact-string patcher for bite mutations.

    python _patch.py <file> <old> <new>

Refuses to run unless <old> occurs EXACTLY ONCE. A mutation that silently matched zero times (or
matched three places and changed all of them) would make the bite test prove something other than what
it claims — which is the failure mode this whole harness exists to prevent.
"""
import sys

if len(sys.argv) != 4:
    sys.exit("usage: _patch.py <file> <old> <new>")

path, old, new = sys.argv[1], sys.argv[2], sys.argv[3]
with open(path, encoding="utf-8", newline="") as fh:
    text = fh.read()

count = text.count(old)
if count != 1:
    sys.exit("PATCH TARGET OCCURS %d TIME(S) (expected exactly 1) in %s" % (count, path))

with open(path, "w", encoding="utf-8", newline="") as fh:
    fh.write(text.replace(old, new))

print("patched %s" % path)
