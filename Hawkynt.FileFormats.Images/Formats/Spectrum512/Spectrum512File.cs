using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Spectrum512;

/// <summary>In-memory representation of a Spectrum 512 image (Atari ST 512-color).</summary>
public readonly record struct Spectrum512File : IImageFormatReader<Spectrum512File>, IImageToRawImage<Spectrum512File>, IImageFromRawImage<Spectrum512File>, IImageFormatWriter<Spectrum512File> {

  static string IImageFormatMetadata<Spectrum512File>.PrimaryExtension => ".spu";
  static string[] IImageFormatMetadata<Spectrum512File>.FileExtensions => [".spu"];
  static Spectrum512File IImageFormatReader<Spectrum512File>.FromSpan(ReadOnlySpan<byte> data) => Spectrum512Reader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<Spectrum512File>.VideoModes => [new("Default", [(320, 199)])];
  static byte[] IImageFormatWriter<Spectrum512File>.ToBytes(Spectrum512File file) => Spectrum512Writer.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public Spectrum512Variant Variant { get; init; }
  public byte[] PixelData { get; init; }
  public short[][] Palettes { get; init; }

  /// <summary>Colours one scanline can name; sixteen at a time, in three overlapping zones.</summary>
  public const int PaletteEntriesPerLine = 48;

  /// <summary>
  /// The palette entry a pixel reads, given its four-bit index and where it sits on the scanline.
  /// </summary>
  /// <remarks>
  /// This is the whole format. The ST shows sixteen colours at once, but its palette registers can
  /// be rewritten while a scanline is being drawn, so Spectrum 512 stores three sets of sixteen per
  /// line and switches between them partway across. What makes it more than three fixed thirds is
  /// that each colour switches at its own position — a register cannot be reloaded until the beam
  /// has passed the pixels still using it, and the registers are reloaded in index order, so the
  /// boundary for index <c>c</c> falls ten pixels further right for each step, nudged by six for
  /// the odd ones. Reading it as fixed thirds puts a sixth of the picture in the wrong colours.
  /// </remarks>
  public static int PaletteEntryFor(int index, int x) {
    var boundary = index * 10 + 1 - (index & 1) * 6;

    return x >= boundary + 160 ? index + 32
      : x >= boundary ? index + 16
      : index;
  }

  public static RawImage ToRawImage(Spectrum512File file) {

    const int width = 320;
    var height = file.Height;
    var chunky = PlanarConverter.AtariStToChunky(file.PixelData, width, height, 4);
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var palette = file.Palettes[y];
      for (var x = 0; x < width; ++x) {
        var entry = palette[PaletteEntryFor(chunky[y * width + x], x)] & 0x0FFF;
        var offset = (y * width + x) * 3;
        rgb[offset] = ChannelScaling.Expand3((entry >> 8) & 7);
        rgb[offset + 1] = ChannelScaling.Expand3((entry >> 4) & 7);
        rgb[offset + 2] = ChannelScaling.Expand3(entry & 7);
      }
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  public static Spectrum512File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      image = PixelConverter.Convert(image, PixelFormat.Rgb24);
    if (image.Width != 320)
      throw new ArgumentException("Spectrum 512 images must be exactly 320 pixels wide.", nameof(image));
    if (image.Height != 199)
      throw new ArgumentException("Spectrum 512 images must be exactly 199 pixels tall.", nameof(image));

    const int width = 320;
    const int height = 199;
    const int paletteEntriesPerLine = 48;
    var palettes = new short[height][];
    var chunky = new byte[width * height];

    for (var y = 0; y < height; ++y) {
      var colorMap = new Dictionary<short, byte>();
      var palette = new short[paletteEntriesPerLine];
      var colorCount = 0;

      for (var x = 0; x < width; ++x) {
        var offset = (y * width + x) * 3;
        var r = image.PixelData[offset] * 7 / 255;
        var g = image.PixelData[offset + 1] * 7 / 255;
        var b = image.PixelData[offset + 2] * 7 / 255;
        var stColor = (short)((r << 8) | (g << 4) | b);

        if (!colorMap.TryGetValue(stColor, out var index)) {
          if (colorCount < 16) {
            index = (byte)colorCount;
            palette[colorCount] = stColor;
            colorMap[stColor] = index;
            ++colorCount;
          } else {
            // More than 16 unique colors on this scanline; find closest match
            index = _FindClosestColor(stColor, palette, colorCount);
          }
        }

        chunky[y * width + x] = index;
      }

      palettes[y] = palette;
    }

    var planar = PlanarConverter.ChunkyToAtariSt(chunky, width, height, 4);

    return new() {
      Width = width,
      Height = height,
      PixelData = planar,
      Palettes = palettes,
    };
  }

  private static byte _FindClosestColor(short target, short[] palette, int count) {
    var tr = (target >> 8) & 0x07;
    var tg = (target >> 4) & 0x07;
    var tb = target & 0x07;
    var bestIndex = (byte)0;
    var bestDist = int.MaxValue;

    for (var i = 0; i < count; ++i) {
      var entry = palette[i];
      var dr = ((entry >> 8) & 0x07) - tr;
      var dg = ((entry >> 4) & 0x07) - tg;
      var db = (entry & 0x07) - tb;
      var dist = dr * dr + dg * dg + db * db;
      if (dist >= bestDist)
        continue;

      bestDist = dist;
      bestIndex = (byte)i;
    }

    return bestIndex;
  }
}
