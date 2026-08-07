#!/bin/bash
# Decodes a directory of samples with one third-party tool, leaving one PPM per file.
#
# Each tool is asked separately so a slow one can be re-run on its own: IrfanView goes through Wine
# and pays a process start per file, which is minutes where the others are seconds.
#
#   decode-with.sh recoil|xnview|irfanview <sample-dir> <output-dir>
#
# Tool locations come from the environment, and a tool that is not configured is skipped rather than
# reported as failing to read anything — an absent tool and a tool that refuses a file look identical
# otherwise, and that difference is the whole point of the comparison.
#
#   RECOIL2PNG   path to recoil2png
#   NCONVERT     path to XnView's nconvert
#   IRFANVIEW    Windows path of i_view64.exe, with WINEPREFIX set to the prefix holding it
#
# Every conversion forces -depth 8, and leaving it off is not a cosmetic difference.
#
# These tools write a PNG at whatever depth the picture needs, so a four-colour one comes out as a
# two-bit palette PNG whose entries are ordinary eight-bit colours. Converting that to PPM without
# saying otherwise makes ImageMagick write "maxval 3" and store the palette *indices* 0..3 in place
# of the colours. The comparison then scales those back by 255/3 — because a low maxval usually does
# mean a low-depth picture — and a decode of 0x44, 0x88, 0xCC comes back as 0x55, 0xAA, 0xFF.
#
# That is the tool being misread rather than the tool disagreeing, and it was reported as "same
# picture, other colours" for every format whose palette happens to be small.
set -u

tool=${1:?tool}
samples=${2:?sample directory}
out=${3:?output directory}
mkdir -p "$out"

case "$tool" in
recoil)
  [ -x "${RECOIL2PNG:-}" ] || { echo "RECOIL2PNG not set"; exit 2; }
  for f in "$samples"/*; do
    rm -f /tmp/parity-rc.png
    "$RECOIL2PNG" -o /tmp/parity-rc.png "$f" 2>/dev/null
    [ -f /tmp/parity-rc.png ] && magick /tmp/parity-rc.png -depth 8 "$out/$(basename "$f").ppm" 2>/dev/null
  done
  ;;
xnview)
  [ -x "${NCONVERT:-}" ] || { echo "NCONVERT not set"; exit 2; }
  for f in "$samples"/*; do
    rm -f /tmp/parity-xn.png
    "$NCONVERT" -quiet -out png -o /tmp/parity-xn.png "$f" >/dev/null 2>&1
    [ -f /tmp/parity-xn.png ] && magick /tmp/parity-xn.png -depth 8 "$out/$(basename "$f").ppm" 2>/dev/null
  done
  ;;
irfanview)
  [ -n "${IRFANVIEW:-}" ] && [ -n "${WINEPREFIX:-}" ] || { echo "IRFANVIEW or WINEPREFIX not set"; exit 2; }
  export WINEDEBUG=-all
  for f in "$samples"/*; do
    rm -f /tmp/parity-iv.bmp
    # Wine reaches the host filesystem through Z:, and /silent stops a dialog waiting on a person.
    timeout 40 wine "$IRFANVIEW" "Z:$(echo "$f" | tr '/' '\\')" '/convert=Z:\tmp\parity-iv.bmp' /silent >/dev/null 2>&1
    [ -f /tmp/parity-iv.bmp ] && magick /tmp/parity-iv.bmp -depth 8 "$out/$(basename "$f").ppm" 2>/dev/null
  done
  ;;
*)
  echo "unknown tool: $tool"; exit 2
  ;;
esac

echo "$tool decoded $(ls "$out" | wc -l) of $(ls "$samples" | wc -l) samples"
