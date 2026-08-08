#!/bin/bash
# Fetches one sample from each format directory we have no sample of that extension for.
#
#   Tools/parity/fetch-samples.sh < list-of-directory-names
#
# The archive is somebody's server, so this asks slowly — one listing and at most one file per
# format, with a pause between. It takes whatever file the directory lists first, which is often a
# readme or a program rather than a picture, so expect roughly a third of what comes back to be
# unusable; that is still cheaper than choosing by hand.
#
# It exists because the corpus was the limit on what could be said. Three hundred and twenty formats
# had a sample and four hundred and twenty-one did not, and "we support 741 formats" cannot be
# checked against a corpus drawn from the ones we already read.
# Where the corpus and the tools live. Override it to keep them somewhere else; the default is a
# scratch directory beside the repository rather than inside it, so a corpus of somebody else's
# sample files never lands in version control.
S="${PARITY_WORK:-${TMPDIR:-/tmp}/pngcrush-parity}"
ROOT="https://telparia.com/fileFormatSamples/image"
OUT="$S/more"
mkdir -p "$OUT"

# Extensions we already hold, so a directory offering only those is skipped without a second request.
ls "$S/real" | sed 's/.*\.//' | tr 'A-Z' 'a-z' | sort -u > /tmp/have_ext.txt

n=0
while read -r dir; do
  [ -z "$dir" ] && continue
  n=$((n+1))
  listing=$(timeout 30 curl -sL "$ROOT/$dir/" 2>/dev/null) || continue
  # Files, with their sizes, as the index lists them.
  file=$(printf '%s' "$listing" \
    | grep -oP 'href="\K[^"?/][^"]*' \
    | grep -vE '/$|^\.\.' \
    | head -40 \
    | while read -r f; do echo "$f"; done \
    | head -1)
  [ -z "$file" ] && continue

  ext=$(printf '%s' "$file" | sed 's/.*\.//' | tr 'A-Z' 'a-z')
  # Only bother when it is an extension we hold no sample of.
  grep -qx "$ext" /tmp/have_ext.txt && continue

  target="$OUT/$(printf '%s' "$dir" | tr -d '/')_$file"
  [ -e "$target" ] && continue
  timeout 60 curl -sL -o "$target" "$ROOT/$dir/$file" 2>/dev/null
  # Anything that came back as an error page rather than a file is no use.
  [ -s "$target" ] || rm -f "$target"
  sleep 0.4
done

echo "fetched $(ls "$OUT" 2>/dev/null | wc -l) new samples from $n directories"
