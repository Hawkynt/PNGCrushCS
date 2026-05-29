using System.IO;

namespace FileFormat.Core;

/// <summary>
/// Helpers for the <c>.pal</c> sidecar convention — preserves a chosen palette alongside
/// indexed formats that don't store one on-disk (NES CHR, GameBoy tile, ZX font data, etc.).
/// </summary>
/// <remarks>
/// Sidecar format: raw packed RGB triplets <c>[R0,G0,B0,R1,G1,B1,...]</c>, length = <c>PaletteCount * 3</c> bytes.
/// Matches the convention used by retro tile editors (YY-CHR, NES Screen Tool, etc.).
/// </remarks>
public static class PaletteSidecar {

  /// <summary>The suffix appended to the main file path to form the sidecar path.</summary>
  public const string SidecarSuffix = ".pal";

  /// <summary>Writes <c>&lt;filePath&gt;.pal</c> with the indexed image's palette. Best-effort —
  /// returns <c>false</c> on failure or when no sidecar is appropriate (non-indexed, no palette).</summary>
  public static bool TryWrite(string filePath, RawImage? raw) {
    if (raw == null) return false;
    if (!raw.IsIndexed) return false;
    if (raw.Palette is not { Length: > 0 } pal) return false;
    if (raw.PaletteCount <= 0) return false;
    var bytesToWrite = System.Math.Min(pal.Length, raw.PaletteCount * 3);
    if (bytesToWrite <= 0) return false;
    try {
      File.WriteAllBytes(filePath + SidecarSuffix, pal[..bytesToWrite]);
      return true;
    } catch {
      return false;
    }
  }

  /// <summary>If a <c>.pal</c> sidecar exists next to <paramref name="filePath"/> and <paramref name="raw"/>
  /// is indexed, returns a new <see cref="RawImage"/> with the sidecar palette applied; otherwise returns
  /// <paramref name="raw"/> unchanged.</summary>
  public static RawImage Apply(string filePath, RawImage raw) {
    if (!raw.IsIndexed) return raw;
    var sidecarPath = filePath + SidecarSuffix;
    if (!File.Exists(sidecarPath)) return raw;
    byte[] sidecar;
    try { sidecar = File.ReadAllBytes(sidecarPath); }
    catch { return raw; }
    if (sidecar.Length == 0 || sidecar.Length % 3 != 0) return raw;

    return new RawImage {
      Width = raw.Width,
      Height = raw.Height,
      Format = raw.Format,
      PixelData = raw.PixelData,
      Palette = sidecar,
      PaletteCount = sidecar.Length / 3,
      AlphaTable = raw.AlphaTable,
    };
  }
}
