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
    [ -f /tmp/parity-rc.png ] && magick /tmp/parity-rc.png "$out/$(basename "$f").ppm" 2>/dev/null
  done
  ;;
xnview)
  [ -x "${NCONVERT:-}" ] || { echo "NCONVERT not set"; exit 2; }
  for f in "$samples"/*; do
    rm -f /tmp/parity-xn.png
    "$NCONVERT" -quiet -out png -o /tmp/parity-xn.png "$f" >/dev/null 2>&1
    [ -f /tmp/parity-xn.png ] && magick /tmp/parity-xn.png "$out/$(basename "$f").ppm" 2>/dev/null
  done
  ;;
irfanview)
  [ -n "${IRFANVIEW:-}" ] && [ -n "${WINEPREFIX:-}" ] || { echo "IRFANVIEW or WINEPREFIX not set"; exit 2; }
  export WINEDEBUG=-all
  for f in "$samples"/*; do
    rm -f /tmp/parity-iv.bmp
    # Wine reaches the host filesystem through Z:, and /silent stops a dialog waiting on a person.
    timeout 40 wine "$IRFANVIEW" "Z:$(echo "$f" | tr '/' '\\')" '/convert=Z:\tmp\parity-iv.bmp' /silent >/dev/null 2>&1
    [ -f /tmp/parity-iv.bmp ] && magick /tmp/parity-iv.bmp "$out/$(basename "$f").ppm" 2>/dev/null
  done
  ;;
*)
  echo "unknown tool: $tool"; exit 2
  ;;
esac

echo "$tool decoded $(ls "$out" | wc -l) of $(ls "$samples" | wc -l) samples"
