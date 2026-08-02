#!/bin/bash
# Compares what Tom's Editor says it reads against what we say we read.
#
# Tom's is a web service with a daily conversion limit, so it cannot be swept over a corpus the way
# an installed tool can. Its catalogue page costs nothing against that limit, though, and answers the
# coverage half of the question on its own: anything it lists and we do not is a gap, whatever the
# per-file agreement turns out to be.
#
# What this does not measure is whether we decode those formats the same way it does. That needs a
# conversion apiece and the limit stops it after a handful; run the conformance suite with
# TOMSEDITOR set to spend the day's allowance on the formats no installed tool can judge.
set -u

root=$(cd "$(dirname "$0")/../.." && pwd)
page=$(mktemp)
theirs=$(mktemp)
ours=$(mktemp)
trap 'rm -f "$page" "$theirs" "$ours"' EXIT

curl -fsS -A "Mozilla/5.0" "https://tomseditor.com/convert/supported-formats" -o "$page" || {
  echo "could not reach the catalogue"
  exit 2
}

# The page names each format with a leading dot, which is what separates them from the prose around
# them — and catches some of the prose too, so the result is a ceiling on what it claims.
python3 - "$page" > "$theirs" <<'PY'
import re, sys
text = re.sub(r"<[^>]+>", " ", open(sys.argv[1], encoding="utf-8", errors="replace").read())
for extension in sorted({m.group(0).lower() for m in re.finditer(r"\.[A-Za-z0-9]{1,6}\b", text)}):
    print(extension)
PY

dotnet run --project "$root/Tools/parity/Decode" -c Release -- --extensions 2>/dev/null | grep '^\.' \
  | LC_ALL=C sort -u > "$ours"
LC_ALL=C sort -u -o "$theirs" "$theirs"

echo "it lists $(wc -l < "$theirs") dotted tokens; we claim $(wc -l < "$ours") extensions"
echo
echo "listed there and not here:"
LC_ALL=C comm -13 "$ours" "$theirs" | sed 's/^/  /'
