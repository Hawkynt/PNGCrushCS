using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace FileFormat.Core;

/// <summary>
/// Helpers for the <c>.pal</c> sidecar convention — preserves a chosen palette alongside
/// indexed formats that don't store one on-disk (NES CHR, GameBoy tile, ZX font data, etc.).
/// </summary>
/// <remarks>
/// <para>
/// <b>Write format:</b> JASC-PAL — the de facto standard text palette format originated by Paint Shop Pro
/// and consumed by GIMP, GrafX2, Aseprite, Pro Motion NG, ImageJ, and most other paint/image tools.
/// </para>
/// <code>
/// JASC-PAL
/// 0100
/// N
/// R G B
/// R G B
/// ...
/// </code>
/// <para>
/// <b>Read format:</b> sniffs the file — JASC text format (preferred) or legacy raw RGB binary
/// (<c>[R0,G0,B0,R1,G1,B1,...]</c>) used by pre-JASC versions of this sidecar.
/// </para>
/// </remarks>
public static class PaletteSidecar {

  /// <summary>The suffix appended to the main file path to form the sidecar path.</summary>
  public const string SidecarSuffix = ".pal";

  private const string _JascHeader = "JASC-PAL";
  private const string _JascVersion = "0100";

  /// <summary>Writes <c>&lt;filePath&gt;.pal</c> with the indexed image's palette in JASC-PAL text format.
  /// Best-effort — returns <c>false</c> on failure or when no sidecar is appropriate (non-indexed, no palette).</summary>
  public static bool TryWrite(string filePath, RawImage? raw) {
    if (raw == null) return false;
    if (!raw.IsIndexed) return false;
    if (raw.Palette is not { Length: > 0 } pal) return false;
    if (raw.PaletteCount <= 0) return false;
    var entries = System.Math.Min(pal.Length / 3, raw.PaletteCount);
    if (entries <= 0) return false;

    try {
      var sb = new StringBuilder(_JascHeader.Length + _JascVersion.Length + entries * 14);
      // JASC-PAL uses CRLF on Windows by convention; LF works too but CRLF is what every editor emits.
      sb.Append(_JascHeader).Append("\r\n");
      sb.Append(_JascVersion).Append("\r\n");
      sb.Append(entries.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
      for (var i = 0; i < entries; ++i) {
        sb.Append(pal[i * 3].ToString(CultureInfo.InvariantCulture)).Append(' ')
          .Append(pal[i * 3 + 1].ToString(CultureInfo.InvariantCulture)).Append(' ')
          .Append(pal[i * 3 + 2].ToString(CultureInfo.InvariantCulture)).Append("\r\n");
      }
      File.WriteAllText(filePath + SidecarSuffix, sb.ToString(), Encoding.ASCII);
      return true;
    } catch {
      return false;
    }
  }

  /// <summary>If a <c>.pal</c> sidecar exists next to <paramref name="filePath"/> and <paramref name="raw"/>
  /// is indexed, returns a new <see cref="RawImage"/> with the sidecar palette applied; otherwise returns
  /// <paramref name="raw"/> unchanged. Accepts JASC-PAL text format or legacy raw-RGB binary.</summary>
  public static RawImage Apply(string filePath, RawImage raw) {
    if (!raw.IsIndexed) return raw;
    var sidecarPath = filePath + SidecarSuffix;
    if (!File.Exists(sidecarPath)) return raw;
    byte[] file;
    try { file = File.ReadAllBytes(sidecarPath); }
    catch { return raw; }
    if (file.Length == 0) return raw;

    var palette = _TryParseJasc(file) ?? _TryParseRawBinary(file);
    if (palette == null) return raw;

    return new RawImage {
      Width = raw.Width,
      Height = raw.Height,
      Format = raw.Format,
      PixelData = raw.PixelData,
      Palette = palette,
      PaletteCount = palette.Length / 3,
      AlphaTable = raw.AlphaTable,
    };
  }

  /// <summary>Parses JASC-PAL text content. Returns packed RGB bytes (<c>[R,G,B,R,G,B,...]</c>) on success, or <c>null</c>.</summary>
  private static byte[]? _TryParseJasc(byte[] file) {
    // Sniff: JASC-PAL files start with the literal "JASC-PAL" header (with optional UTF-8/UTF-16 BOM).
    var text = _DecodeTextPreservingBom(file);
    if (text == null) return null;
    if (!text.StartsWith(_JascHeader, StringComparison.OrdinalIgnoreCase)) return null;

    var lines = text.Split('\n');
    if (lines.Length < 4) return null;
    // Line 0: "JASC-PAL", line 1: version (typically "0100"), line 2: count, lines 3..: "R G B"
    if (!int.TryParse(lines[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)) return null;
    if (count <= 0 || lines.Length < 3 + count) return null;

    var palette = new byte[count * 3];
    for (var i = 0; i < count; ++i) {
      var parts = lines[3 + i].Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
      if (parts.Length < 3) return null;
      if (!byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r)) return null;
      if (!byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g)) return null;
      if (!byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b)) return null;
      palette[i * 3] = r;
      palette[i * 3 + 1] = g;
      palette[i * 3 + 2] = b;
    }
    return palette;
  }

  /// <summary>Parses legacy raw-RGB binary content (pre-JASC sidecars). Returns the bytes unchanged on success.</summary>
  private static byte[]? _TryParseRawBinary(byte[] file) {
    if (file.Length == 0 || file.Length % 3 != 0) return null;
    return file;
  }

  private static string? _DecodeTextPreservingBom(byte[] file) {
    // Cheap text sniff: must be printable ASCII at offsets 0..7 ("JASC-PAL" or similar).
    if (file.Length < 8) return null;
    for (var i = 0; i < 8; ++i) {
      if (file[i] < 0x09 || file[i] > 0x7E) return null;
    }
    try { return Encoding.ASCII.GetString(file); } catch { return null; }
  }
}
