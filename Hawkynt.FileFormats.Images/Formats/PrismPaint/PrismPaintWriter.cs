using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PrismPaint;

/// <summary>
/// Assembles Atari Falcon Prism Paint file bytes from a PrismPaintFile.
/// </summary>
/// <remarks>
/// What this wrote before matched what the reader then assumed and nothing else: the size in the
/// first four bytes where the signature belongs, a Falcon palette of 256 packed entries, and one
/// byte a pixel. A real file opens with <c>PNT\0</c>, states its size as two big-endian words with
/// the plane count after them, keeps its palette as three words an entry on the VDI's
/// nought-to-a-thousand scale in the VDI's own order, and stores the screen as bitplanes.
/// </remarks>
public static class PrismPaintWriter {

  public static byte[] ToBytes(PrismPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var planes = _PlanesFor(file.Palette);
    var colors = 1 << planes;
    var bytesPerRow = (file.Width + 15) / 16 * 2 * planes;
    var screen = bytesPerRow * file.Height;
    var result = new byte[PrismPaintFile.PaletteOffset + colors * PrismPaintFile.PaletteEntryBytes + screen];

    PrismPaintFile.Signature.CopyTo(result);
    result[4] = 1;
    result[PrismPaintFile.WidthOffset] = (byte)(file.Width >> 8);
    result[PrismPaintFile.WidthOffset + 1] = (byte)file.Width;
    result[PrismPaintFile.HeightOffset] = (byte)(file.Height >> 8);
    result[PrismPaintFile.HeightOffset + 1] = (byte)file.Height;
    result[PrismPaintFile.PlanesOffset] = (byte)(planes >> 8);
    result[PrismPaintFile.PlanesOffset + 1] = (byte)planes;

    var palette = file.Palette ?? [];
    for (var i = 0; i < colors; ++i) {
      var from = AtariStGraphics.VdiToHardwareIndex(i, planes) * 3;
      var at = PrismPaintFile.PaletteOffset + i * PrismPaintFile.PaletteEntryBytes;
      for (var channel = 0; channel < 3; ++channel) {
        var value = from + channel < palette.Length ? palette[from + channel] : 0;
        var scaled = value * PrismPaintFile.PaletteChannelMaximum / 255;
        result[at + channel * 2] = (byte)(scaled >> 8);
        result[at + channel * 2 + 1] = (byte)scaled;
      }
    }

    var planar = PlanarConverter.ChunkyToAtariSt(file.PixelData ?? [], file.Width, file.Height, planes);
    planar.AsSpan(0, Math.Min(planar.Length, screen))
      .CopyTo(result.AsSpan(PrismPaintFile.PaletteOffset + colors * PrismPaintFile.PaletteEntryBytes));

    return result;
  }

  /// <summary>How many bitplanes the palette's entry count calls for.</summary>
  private static int _PlanesFor(byte[]? palette) {
    var entries = (palette?.Length ?? 0) / 3;
    var planes = 1;
    while (planes < 8 && (1 << planes) < entries)
      ++planes;

    return planes;
  }

  public static void ToStream(PrismPaintFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    var bytes = ToBytes(file);
    stream.Write(bytes, 0, bytes.Length);
  }

  public static void ToFile(PrismPaintFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
