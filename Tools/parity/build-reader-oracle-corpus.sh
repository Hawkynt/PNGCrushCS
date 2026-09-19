#!/bin/bash
# Builds a reader-conformance corpus from third-party writers and third-party reference decoders.
#
# This is the other half of build-oracle-corpus.sh. That script asks whether somebody else's reader
# accepts bytes our writer made. This one asks somebody else's writer to make the bytes, asks an
# external decoder what picture those bytes mean, and records the pair so ReaderOracleCorpusTests can
# compare this package's reader with it.
#
#   Tools/parity/build-reader-oracle-corpus.sh [astc|ktx|djvu|bpg|flif|jbig2|gdal|c3d|krita|dcmtk|netpbm|all]
#
# Tool paths may be supplied through the environment variables named in each function below; otherwise
# the ordinary executable name is searched on PATH. Missing tools are skipped, not treated as a
# conformance failure. The corpus lives outside the repository: it is generated evidence, not source.
set -u

S="${PARITY_WORK:-${TMPDIR:-/tmp}/pngcrush-parity}"
C="$S/reader-oracle-corpus"
WIDTH=61
HEIGHT=37

rm -rf "$C"
mkdir -p "$C/source"
MANIFEST="$C/manifest.tsv"
printf '# producer\tdecoder\tformat\tencoded\treference\ttolerance\n' > "$MANIFEST"

_resolve() {
  local configured="$1" fallback="$2"

  if [ -n "$configured" ]; then
    if [ -x "$configured" ]; then
      printf '%s\n' "$configured"
      return 0
    fi
    command -v "$configured" 2>/dev/null && return 0
    return 1
  fi

  command -v "$fallback" 2>/dev/null
}

_need_magick() {
  command -v magick >/dev/null 2>&1 || {
    echo 'reader oracle corpus: ImageMagick is required only to build neutral source/normalisation images'
    exit 2
  }
}

_record() {
  local producer="$1" decoder="$2" format="$3" encoded="$4" reference="$5" tolerance="$6"

  [ -s "$encoded" ] || return 1
  [ -s "$reference" ] || return 1

  printf '%s\t%s\t%s\t%s\t%s\t%s\n' \
    "$producer" "$decoder" "$format" "${encoded#$C/}" "${reference#$C/}" "$tolerance" >> "$MANIFEST"
  echo "$format: $producer -> $decoder"
}

_reference_from_pnm() {
  local source="$1" target="$2"
  magick "$source" -depth 8 "$target" >/dev/null 2>&1
}

_need_magick
SRC="$C/source/source.png"
SRC_PPM="$C/source/source.ppm"
SRC_PBM="$C/source/source.pbm"
SRC_BMP="$C/source/source.bmp"
SRC_GRAY="$C/source/source-gray.png"

magick -size "${WIDTH}x${HEIGHT}" gradient:blue-yellow -colorspace sRGB -alpha off -type TrueColor "$SRC"
magick "$SRC" -alpha off "$SRC_PPM"
magick "$SRC" -colorspace gray -threshold 48% "$SRC_PBM"
magick "$SRC" -alpha off "BMP3:$SRC_BMP"
magick "$SRC" -colorspace gray -depth 8 "$SRC_GRAY"

_astc() {
  local exe out encoded reference
  exe="$(_resolve "${ASTCENC:-}" astcenc)" || { echo 'astcenc: skipped'; return; }
  out="$C/astcenc"; mkdir -p "$out"
  encoded="$out/sample.astc"; reference="$out/reference.png"

  "$exe" -cl "$SRC" "$encoded" 4x4 -medium >/dev/null 2>&1 || return
  "$exe" -dl "$encoded" "$reference" >/dev/null 2>&1 || return
  _record AstcEnc AstcEnc Astc "$encoded" "$reference" 2
}

_ktx() {
  local exe out encoded prefix produced reference
  exe="$(_resolve "${KTX:-}" ktx)" || { echo 'ktx: skipped'; return; }
  out="$C/ktx"; mkdir -p "$out"
  encoded="$out/sample.ktx2"; prefix="$out/reference"; reference="$out/reference.png"

  "$exe" create --format R8G8B8_SRGB "$SRC" "$encoded" >/dev/null 2>&1 || return
  "$exe" extract "$encoded" "$prefix" >/dev/null 2>&1 || return
  produced="$(find "$out" -maxdepth 1 -type f -name 'reference*.png' | head -n 1)"
  [ -n "$produced" ] || return
  [ "$produced" = "$reference" ] || mv "$produced" "$reference"
  _record KtxTools KtxTools Ktx "$encoded" "$reference" 0
}

_djvu() {
  local enc dec out encoded reference
  enc="$(_resolve "${C44:-}" c44)" || { echo 'DjVuLibre/c44: skipped'; return; }
  dec="$(_resolve "${DDJVU:-}" ddjvu)" || { echo 'DjVuLibre/ddjvu: skipped'; return; }
  out="$C/djvu"; mkdir -p "$out"
  encoded="$out/sample.djvu"; reference="$out/reference.png"

  "$enc" "$SRC_PPM" "$encoded" >/dev/null 2>&1 || return
  "$dec" -format=png "$encoded" "$reference" >/dev/null 2>&1 || return
  _record DjVuLibre DjVuLibre DjVu "$encoded" "$reference" 3
}

_bpg() {
  local enc dec out encoded reference
  enc="$(_resolve "${BPGENC:-}" bpgenc)" || { echo 'libbpg/bpgenc: skipped'; return; }
  dec="$(_resolve "${BPGDEC:-}" bpgdec)" || { echo 'libbpg/bpgdec: skipped'; return; }
  out="$C/bpg"; mkdir -p "$out"
  encoded="$out/sample.bpg"; reference="$out/reference.png"

  "$enc" -q 20 -o "$encoded" "$SRC" >/dev/null 2>&1 || return
  "$dec" -o "$reference" "$encoded" >/dev/null 2>&1 || return
  _record LibBpg LibBpg Bpg "$encoded" "$reference" 3
}

_flif() {
  local exe out encoded reference
  exe="$(_resolve "${FLIF:-}" flif)" || { echo 'flif: skipped'; return; }
  out="$C/flif"; mkdir -p "$out"
  encoded="$out/sample.flif"; reference="$out/reference.png"

  "$exe" -e "$SRC" "$encoded" >/dev/null 2>&1 || return
  "$exe" -d "$encoded" "$reference" >/dev/null 2>&1 || return
  _record Flif Flif Flif "$encoded" "$reference" 0
}

_jbig2() {
  local enc dec out encoded reference
  enc="$(_resolve "${JBIG2ENC:-}" jbig2)" || { echo 'jbig2enc: skipped'; return; }
  dec="$(_resolve "${JBIG2DEC:-}" jbig2dec)" || { echo 'jbig2dec: skipped'; return; }
  out="$C/jbig2"; mkdir -p "$out"
  encoded="$out/sample.jb2"; reference="$out/reference.png"

  "$enc" -s "$SRC_PBM" > "$encoded" 2>/dev/null || return
  "$dec" -q -t png -o "$reference" "$encoded" >/dev/null 2>&1 || return
  _record Jbig2Enc Jbig2Dec Jbig2 "$encoded" "$reference" 0
}

_gdal() {
  local exe out encoded reference
  exe="$(_resolve "${GDAL_TRANSLATE:-}" gdal_translate)" || { echo 'GDAL: skipped'; return; }
  out="$C/gdal"; mkdir -p "$out"
  encoded="$out/sample.ntf"; reference="$out/reference.png"

  "$exe" -q -of NITF "$SRC" "$encoded" >/dev/null 2>&1 || return
  "$exe" -q -of PNG "$encoded" "$reference" >/dev/null 2>&1 || return
  _record Gdal Gdal Nitf "$encoded" "$reference" 0
}

_c3d_one() {
  local exe="$1" out="$2" format="$3" extension="$4"
  local encoded="$out/sample$extension" reference="$out/reference-$format.png"

  "$exe" "$SRC_GRAY" -o "$encoded" >/dev/null 2>&1 || return
  "$exe" "$encoded" -o "$reference" >/dev/null 2>&1 || return
  _record Convert3D Convert3D "$format" "$encoded" "$reference" 0
}

_c3d() {
  local exe out
  exe="$(_resolve "${C3D:-}" c3d)" || { echo 'Convert3D: skipped'; return; }
  out="$C/c3d"; mkdir -p "$out"

  _c3d_one "$exe" "$out" Nifti .nii
  _c3d_one "$exe" "$out" Nrrd .nrrd
  _c3d_one "$exe" "$out" MetaImage .mha
  _c3d_one "$exe" "$out" Mrc .mrc
}

_krita() {
  local exe out encoded reference
  exe="$(_resolve "${KRITA:-}" krita)" || { echo 'Krita: skipped'; return; }
  out="$C/krita"; mkdir -p "$out"
  encoded="$out/sample.kra"; reference="$out/reference.png"

  "$exe" "$SRC" --export --export-filename "$encoded" >/dev/null 2>&1 || return
  "$exe" "$encoded" --export --export-filename "$reference" >/dev/null 2>&1 || return
  _record Krita Krita Krita "$encoded" "$reference" 0
}

_dcmtk() {
  local enc dec out encoded reference
  enc="$(_resolve "${IMG2DCM:-}" img2dcm)" || { echo 'DCMTK/img2dcm: skipped'; return; }
  dec="$(_resolve "${DCM2PNM:-}" dcm2pnm)" || { echo 'DCMTK/dcm2pnm: skipped'; return; }
  out="$C/dcmtk"; mkdir -p "$out"
  encoded="$out/sample.dcm"; reference="$out/reference.png"

  "$enc" -i BMP "$SRC_BMP" "$encoded" >/dev/null 2>&1 || return
  "$dec" +on "$encoded" "$reference" >/dev/null 2>&1 || return
  _record Dcmtk Dcmtk Dicom "$encoded" "$reference" 0
}

_netpbm() {
  local to_atk from_atk to_g3 from_g3 out encoded pnm reference
  out="$C/netpbm"; mkdir -p "$out"

  to_atk="$(_resolve "${PBMTOATK:-}" pbmtoatk)" || true
  from_atk="$(_resolve "${ATKTOPBM:-}" atktopbm)" || true
  if [ -n "$to_atk" ] && [ -n "$from_atk" ]; then
    encoded="$out/sample.atk"; pnm="$out/reference-atk.pbm"; reference="$out/reference-atk.png"
    "$to_atk" < "$SRC_PBM" > "$encoded" 2>/dev/null \
      && "$from_atk" "$encoded" > "$pnm" 2>/dev/null \
      && _reference_from_pnm "$pnm" "$reference" \
      && _record Netpbm Netpbm AndrewToolkit "$encoded" "$reference" 0
  else
    echo 'Netpbm ATK pair: skipped'
  fi

  to_g3="$(_resolve "${PBMTOG3:-}" pbmtog3)" || true
  from_g3="$(_resolve "${G3TOPBM:-}" g3topbm)" || true
  if [ -n "$to_g3" ] && [ -n "$from_g3" ]; then
    encoded="$out/sample.g3"; pnm="$out/reference-g3.pbm"; reference="$out/reference-g3.png"
    "$to_g3" < "$SRC_PBM" > "$encoded" 2>/dev/null \
      && "$from_g3" "$encoded" > "$pnm" 2>/dev/null \
      && _reference_from_pnm "$pnm" "$reference" \
      && _record Netpbm Netpbm FaxG3 "$encoded" "$reference" 0
  else
    echo 'Netpbm Group-3 pair: skipped'
  fi
}

case "${1:-all}" in
  astc) _astc;;
  ktx) _ktx;;
  djvu) _djvu;;
  bpg) _bpg;;
  flif) _flif;;
  jbig2) _jbig2;;
  gdal) _gdal;;
  c3d) _c3d;;
  krita) _krita;;
  dcmtk) _dcmtk;;
  netpbm) _netpbm;;
  all) _astc; _ktx; _djvu; _bpg; _flif; _jbig2; _gdal; _c3d; _krita; _dcmtk; _netpbm;;
  *) echo "usage: $0 [astc|ktx|djvu|bpg|flif|jbig2|gdal|c3d|krita|dcmtk|netpbm|all]"; exit 2;;
esac

echo
echo "reader oracle corpus: $C"
echo "manifest: $MANIFEST"
echo "run: READER_ORACLE_CORPUS='$C' dotnet test Tests/Hawkynt.FileFormats.Images.Tests --filter ReaderOracleCorpus"
