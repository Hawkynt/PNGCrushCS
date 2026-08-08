using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.AppleShr;

/// <summary>In-memory representation of an Apple IIgs Super Hi-Res image.</summary>
public readonly record struct AppleShrFile : IImageFormatReader<AppleShrFile>, IImageToRawImage<AppleShrFile>, IImageFromRawImage<AppleShrFile>, IImageFormatWriter<AppleShrFile> {

  static string IImageFormatMetadata<AppleShrFile>.PrimaryExtension => ".shr";
  static string[] IImageFormatMetadata<AppleShrFile>.FileExtensions => [".shr"];
  static AppleShrFile IImageFormatReader<AppleShrFile>.FromSpan(ReadOnlySpan<byte> data) => AppleShrReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<AppleShrFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [new IntegerRange(2, 256)])
  ];
  static byte[] IImageFormatWriter<AppleShrFile>.ToBytes(AppleShrFile file) => AppleShrWriter.ToBytes(file);

  /// <summary>The fixed width of a Super Hi-Res image in pixels (320 mode).</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of a Super Hi-Res image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (32000 pixel + 200 SCB + 56 padding + 512 palette).</summary>
  public const int ExpectedFileSize = 32768;

  /// <summary>Size of the pixel data section in bytes (160 bytes/row x 200 rows).</summary>
  internal const int PixelDataSize = 32000;

  /// <summary>Size of the scanline control byte section.</summary>
  internal const int ScbSize = 200;

  /// <summary>Padding between SCB and palette to reach offset 32256.</summary>
  internal const int PaddingSize = 56;

  /// <summary>Size of the palette section in bytes (16 palettes x 16 entries x 2 bytes).</summary>
  internal const int PaletteSize = 512;

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>Pixel data (32000 bytes, 4bpp packed, 2 pixels per byte, 160 bytes per row).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Scanline control bytes (200 bytes, 1 per scanline, low nibble selects palette 0-15).</summary>
  public byte[] ScanlineControl { get; init; }

  /// <summary>Palette data (512 bytes, 16 palettes x 16 entries x 2 bytes, 0RGB 4 bits each).</summary>
  public byte[] Palette { get; init; }

  /// <summary>Converts this Apple IIgs SHR image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(AppleShrFile file) {

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var paletteIndex = file.ScanlineControl[y] & 0x0F;
      var paletteOffset = paletteIndex * 16 * 2;
      var rowOffset = y * 160;

      for (var x = 0; x < width; ++x) {
        var byteIndex = rowOffset + x / 2;
        int colorIndex;
        if ((x & 1) == 0)
          colorIndex = (file.PixelData[byteIndex] >> 4) & 0x0F;
        else
          colorIndex = file.PixelData[byteIndex] & 0x0F;

        var entryOffset = paletteOffset + colorIndex * 2;
        var entry = file.Palette[entryOffset] | (file.Palette[entryOffset + 1] << 8);
        var r = (byte)(((entry >> 8) & 0x0F) * 17);
        var g = (byte)(((entry >> 4) & 0x0F) * 17);
        var b = (byte)((entry & 0x0F) * 17);

        var pixelOffset = (y * width + x) * 3;
        rgb[pixelOffset] = r;
        rgb[pixelOffset + 1] = g;
        rgb[pixelOffset + 2] = b;
      }
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Builds a super hi-res picture from any image, sampling it to 320x200.</summary>
  /// <remarks>
  /// A scanline names one of sixteen palettes through its control byte, so the picture can hold 256
  /// colours even though a pixel only has four bits. The palettes are therefore built from the lines
  /// rather than from the picture: a line whose colours already fit a palette in use shares it, and
  /// one that does not is given a palette of its own while any are left. Quantising the whole
  /// picture to sixteen first would throw away fifteen sixteenths of what the format can show.
  /// </remarks>
  public static AppleShrFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    const int width = FixedWidth;
    const int height = FixedHeight;
    var source = image.SampleTo(width, height).EnsureFormat(PixelFormat.Rgb24);
    var pixelData = new byte[PixelDataSize];
    var scanlineControl = new byte[ScbSize];
    var paletteData = new byte[PaletteSize];
    var banks = new List<int[]>();

    for (var y = 0; y < height; ++y) {
      var line = _NarrowLine(source.PixelData, y, width);
      var wanted = _DistinctColors(line);
      var bank = _ChooseBank(banks, wanted);

      scanlineControl[y] = (byte)bank;
      var entries = banks[bank];
      for (var x = 0; x < width; ++x) {
        var index = _IndexOf(entries, line[x]);
        var at = y * 160 + x / 2;
        pixelData[at] |= (byte)((x & 1) == 0 ? index << 4 : index);
      }
    }

    for (var bank = 0; bank < banks.Count; ++bank)
    for (var entry = 0; entry < AppleIIGSGraphics.ColorCount; ++entry) {
      var color = banks[bank][entry];
      var at = bank * AppleIIGSGraphics.PaletteSize + entry * 2;

      // Green and blue share the low byte of the word; red is alone in the high one.
      paletteData[at] = (byte)((((color >> 4) & 15) << 4) | (color & 15));
      paletteData[at + 1] = (byte)((color >> 8) & 15);
    }

    return new() { PixelData = pixelData, ScanlineControl = scanlineControl, Palette = paletteData };
  }

  /// <summary>One scanline as the twelve-bit colours the hardware stores, four bits a channel.</summary>
  /// <remarks>
  /// Rounded, not truncated: the decoder widens a nibble by multiplying by seventeen, so dividing by
  /// sixteen to get it back lands a step low on everything but nought and full scale.
  /// </remarks>
  private static int[] _NarrowLine(byte[] rgb, int y, int width) {
    var line = new int[width];
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      line[x] = (((rgb[at] + 8) / 17) << 8) | (((rgb[at + 1] + 8) / 17) << 4) | ((rgb[at + 2] + 8) / 17);
    }

    return line;
  }

  private static List<int> _DistinctColors(int[] line) {
    var distinct = new List<int>(AppleIIGSGraphics.ColorCount);
    foreach (var color in line)
      if (!distinct.Contains(color)) {
        distinct.Add(color);
        if (distinct.Count == AppleIIGSGraphics.ColorCount)
          break;
      }

    return distinct;
  }

  /// <summary>The palette to draw a line with: one already holding its colours, a fresh one, or the nearest.</summary>
  private static int _ChooseBank(List<int[]> banks, List<int> wanted) {
    for (var bank = 0; bank < banks.Count; ++bank) {
      var fits = true;
      foreach (var color in wanted)
        if (Array.IndexOf(banks[bank], color) < 0) {
          fits = false;
          break;
        }

      if (fits)
        return bank;
    }

    if (banks.Count < AppleIIGSGraphics.ColorCount) {
      var entries = new int[AppleIIGSGraphics.ColorCount];
      for (var i = 0; i < wanted.Count; ++i)
        entries[i] = wanted[i];

      // Unused entries repeat the last colour rather than staying black, so a line that later
      // matches this palette is not offered a black it never asked for.
      for (var i = Math.Max(1, wanted.Count); i < entries.Length; ++i)
        entries[i] = entries[i - 1];

      banks.Add(entries);
      return banks.Count - 1;
    }

    var best = 0;
    var bestCost = long.MaxValue;
    for (var bank = 0; bank < banks.Count; ++bank) {
      long cost = 0;
      foreach (var color in wanted)
        cost += _Distance(color, banks[bank][_Nearest(banks[bank], color)]);

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = bank;
    }

    return best;
  }

  /// <summary>Where a colour sits in a palette, or the nearest entry when it is not there at all.</summary>
  private static byte _IndexOf(int[] entries, int color) {
    var at = Array.IndexOf(entries, color);

    return (byte)(at >= 0 ? at : _Nearest(entries, color));
  }

  private static int _Nearest(int[] entries, int color) {
    var best = 0;
    var bestCost = int.MaxValue;

    for (var i = 0; i < entries.Length; ++i) {
      var cost = _Distance(color, entries[i]);
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = i;
    }

    return best;
  }

  private static int _Distance(int left, int right) {
    var dr = ((left >> 8) & 15) - ((right >> 8) & 15);
    var dg = ((left >> 4) & 15) - ((right >> 4) & 15);
    var db = (left & 15) - (right & 15);

    return dr * dr + dg * dg + db * db;
  }

}
