#!/bin/bash
# Builds a test corpus by asking each third-party tool to write every format it can, then runs this
# library's registry over the result.
#
#   Tools/parity/build-oracle-corpus.sh [imagemagick|ffmpeg|nconvert|all]
#
# The point is that comparing format *names* between tools does not work and has misled this project
# before. ImageMagick's "format" column, ffmpeg's demuxer names and this library's file extensions
# are three different namespaces: ImageMagick calls JPEG XL "jxl" and ffmpeg calls it "jpegxl", and
# a diff of those lists reports a gap where there is none. It also reports gaps for ImageMagick's
# synthetic sources (canvas, gradient, plasma) and its video containers, none of which is a picture
# format anybody has a file of.
#
# So the question is asked the only way that answers it: have the tool write a file, confirm the tool
# can read its own file back, and then see whether we can read it. A file that exists and that its
# author agrees is valid is evidence; a name in a list is not.
#
# The corpus lands outside the repository. Everything in it is written by somebody else's encoder and
# none of it belongs in version control.
S="${PARITY_WORK:-${TMPDIR:-/tmp}/pngcrush-parity}"
C="$S/oracle-corpus"
NCONVERT="${NCONVERT:-}"

# Small, and not square, and not a multiple of eight either way: three properties that between them
# catch padding, row-order and block-alignment mistakes that a 64x64 test card sails past.
WIDTH=61
HEIGHT=37

set -u
mkdir -p "$C"
SRC="$C/src.png"
[ -f "$SRC" ] || magick -size "${WIDTH}x${HEIGHT}" gradient:blue-yellow -colorspace sRGB "$SRC"

# ImageMagick's own pseudo-formats: sources that generate a picture rather than store one, its
# internal caches, and the protocol handlers. Asking these to round-trip is meaningless.
_im_is_pseudo() {
  case "$1" in
    canvas|caption|clip|data|file|fractal|gradient|group|hald|http|https|inline|label|mask|null) return 0;;
    pango|pattern|plasma|radial-gradient|stegano|text|txt|xc|ftxt|scan|screenshot|show|win|x|print) return 0;;
    *) return 1;;
  esac
}

_imagemagick() {
  local out="$C/imagemagick" kept=0
  rm -rf "$out"; mkdir -p "$out"

  while read -r f; do
    _im_is_pseudo "$f" && continue

    # Written, then re-read by its own author. A format ImageMagick writes and then cannot open is
    # its problem rather than ours, and putting it in the corpus would only manufacture a failure.
    if magick "$SRC" "$out/s.$f" >/dev/null 2>&1 && [ -s "$out/s.$f" ] \
       && magick identify "$out/s.$f" >/dev/null 2>&1; then
      kept=$((kept + 1))
    else
      rm -f "$out/s.$f"
    fi
  done < <(magick -list format 2>/dev/null | awk 'NR>2 && $3 ~ /^rw/ {gsub(/\*/,"",$1); print tolower($1)}' | grep -E '^[a-z0-9]+$' | sort -u)

  echo "imagemagick: $kept files"
}

_ffmpeg() {
  local out="$C/ffmpeg" kept=0 ext
  rm -rf "$out"; mkdir -p "$out"

  # ffmpeg names a codec, not an extension, and the muxer is chosen from the name we give the file.
  for m in bmp dds dpx exr gif hdr j2k jpeg jpegls jpegxl pam pbm pcx pfm pgm pgmyuv pgx phm \
           png ppm psd qoi sgi sunrast tiff vbn webp xbm xpm xwd tga apng; do
    case "$m" in
      jpeg) ext=jpg;; sunrast) ext=ras;; jpegls) ext=jls;; jpegxl) ext=jxl;; *) ext="$m";;
    esac

    if ffmpeg -y -loglevel error -i "$SRC" -frames:v 1 "$out/f.$ext" >/dev/null 2>&1 \
       && [ -s "$out/f.$ext" ] && ffmpeg -loglevel error -i "$out/f.$ext" -f null - >/dev/null 2>&1; then
      kept=$((kept + 1))
    else
      rm -f "$out/f.$ext"
    fi
  done

  echo "ffmpeg: $kept files"
}

_nconvert() {
  local out="$C/nconvert"
  if [ -z "$NCONVERT" ] || [ ! -x "$NCONVERT" ]; then
    echo "nconvert: skipped, set NCONVERT to its path"
    return
  fi

  rm -rf "$out"; mkdir -p "$out"

  # The name it writes under and the extension that name's files carry are different columns of its
  # own catalogue, and it will happily write a file under a name nothing dispatches on. So the
  # extension comes from Formats.txt beside the binary rather than from the format name.
  local formats="$(dirname "$NCONVERT")/Formats.txt"
  if [ ! -f "$formats" ]; then
    echo "nconvert: skipped, no Formats.txt beside it"
    return
  fi

  NCONVERT="$NCONVERT" FORMATS="$formats" OUT="$out" SRC="$SRC" python3 - <<'PY'
import os, re, subprocess

nconvert, formats, out, src = os.environ['NCONVERT'], os.environ['FORMATS'], os.environ['OUT'], os.environ['SRC']
primary = {}
for line in open(formats, encoding='latin-1'):
    m = re.match(r'^\[([^\]]+)\]\s{2,}(.{0,40}?)\s{2,}(.*)$', line.rstrip('\n'))
    if not m:
        continue
    exts = re.split(r'\s{3,}', m.group(3).strip(), maxsplit=1)[0].split()
    if exts:
        primary.setdefault(m.group(1), exts[0])

kept = 0
for name, ext in primary.items():
    path = f'{out}/n_{name}.{ext}'
    subprocess.run([nconvert, '-out', name, '-o', path, src], capture_output=True)
    if os.path.exists(path) and os.path.getsize(path) > 0:
        kept += 1
    elif os.path.exists(path):
        os.remove(path)

print(f'nconvert: {kept} files')
PY
}

case "${1:-all}" in
  imagemagick) _imagemagick;;
  ffmpeg) _ffmpeg;;
  nconvert) _nconvert;;
  all) _imagemagick; _ffmpeg; _nconvert;;
  *) echo "usage: $0 [imagemagick|ffmpeg|nconvert|all]"; exit 2;;
esac

echo
echo "corpus in $C"
echo "now: dotnet run --project Tools/parity/Decode -- $C/<tool> $C/<tool>-decoded"
echo "and for anything that did not decode: Decode --why <file>"
