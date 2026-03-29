using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.HighresMedium;

/// <summary>In-memory representation of an Atari ST Highres Medium interlaced image (640x200, 2 frames blended).</summary>
public sealed class HighresMediumFile : IImageFileFormat<HighresMediumFile> {

  /// <summary>Total file size: 2 frames of (32-byte palette + 32000 bytes planar) = 64064 bytes.</summary>
  public const int FileSize = HighresMediumHeader.FrameSize * 2;

  /// <summary>Image width (always 640).</summary>
  public const int ImageWidth = 640;

  /// <summary>Image height (always 200).</summary>
  public const int ImageHeight = 200;

  /// <summary>Number of bitplanes per frame (always 2 for medium resolution).</summary>
  public const int NumPlanes = 2;

  /// <summary>Number of usable palette colors per frame (always 4 for 2 planes).</summary>
  public const int ColorCount = 4;

  static string IImageFileFormat<HighresMediumFile>.PrimaryExtension => ".hrm";
  static string[] IImageFileFormat<HighresMediumFile>.FileExtensions => [".hrm"];
  static HighresMediumFile IImageFileFormat<HighresMediumFile>.FromFile(FileInfo file) => HighresMediumReader.FromFile(file);
  static HighresMediumFile IImageFileFormat<HighresMediumFile>.FromBytes(byte[] data) => HighresMediumReader.FromBytes(data);
  static HighresMediumFile IImageFileFormat<HighresMediumFile>.FromStream(Stream stream) => HighresMediumReader.FromStream(stream);
  static byte[] IImageFileFormat<HighresMediumFile>.ToBytes(HighresMediumFile file) => HighresMediumWriter.ToBytes(file);

  /// <summary>16-entry palette for frame 1 (only first 4 entries used).</summary>
  public short[] Palette1 { get; init; } = new short[16];

  /// <summary>32000 bytes of Atari ST word-interleaved 2-plane planar pixel data for frame 1.</summary>
  public byte[] PixelData1 { get; init; } = new byte[32000];

  /// <summary>16-entry palette for frame 2 (only first 4 entries used).</summary>
  public short[] Palette2 { get; init; } = new short[16];

  /// <summary>32000 bytes of Atari ST word-interleaved 2-plane planar pixel data for frame 2.</summary>
  public byte[] PixelData2 { get; init; } = new byte[32000];

  public static RawImage ToRawImage(HighresMediumFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var chunky1 = PlanarConverter.AtariStToChunky(file.PixelData1, ImageWidth, ImageHeight, NumPlanes);
    var chunky2 = PlanarConverter.AtariStToChunky(file.PixelData2, ImageWidth, ImageHeight, NumPlanes);

    var palCount1 = Math.Min(ColorCount, file.Palette1.Length);
    var palCount2 = Math.Min(ColorCount, file.Palette2.Length);
    var rgb1 = PlanarConverter.StPaletteToRgb(file.Palette1.AsSpan(0, palCount1));
    var rgb2 = PlanarConverter.StPaletteToRgb(file.Palette2.AsSpan(0, palCount2));

    var pixelCount = ImageWidth * ImageHeight;
    var result = new byte[pixelCount * 3];

    for (var i = 0; i < pixelCount; ++i) {
      var idx1 = chunky1[i];
      var idx2 = chunky2[i];

      byte r1 = 0, g1 = 0, b1 = 0;
      if (idx1 < palCount1) {
        r1 = rgb1[idx1 * 3];
        g1 = rgb1[idx1 * 3 + 1];
        b1 = rgb1[idx1 * 3 + 2];
      }

      byte r2 = 0, g2 = 0, b2 = 0;
      if (idx2 < palCount2) {
        r2 = rgb2[idx2 * 3];
        g2 = rgb2[idx2 * 3 + 1];
        b2 = rgb2[idx2 * 3 + 2];
      }

      var offset = i * 3;
      result[offset] = (byte)((r1 + r2) / 2);
      result[offset + 1] = (byte)((g1 + g2) / 2);
      result[offset + 2] = (byte)((b1 + b2) / 2);
    }

    return new() {
      Width = ImageWidth,
      Height = ImageHeight,
      Format = PixelFormat.Rgb24,
      PixelData = result,
    };
  }

  public static HighresMediumFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException("RawImage must use PixelFormat.Rgb24.", nameof(image));
    if (image.Width != ImageWidth)
      throw new ArgumentException($"Highres Medium images must be exactly {ImageWidth} pixels wide.", nameof(image));
    if (image.Height != ImageHeight)
      throw new ArgumentException($"Highres Medium images must be exactly {ImageHeight} pixels tall.", nameof(image));

    // Both frames get the same image data (quantized to 4 colors)
    var pixelCount = ImageWidth * ImageHeight;
    var chunky = new byte[pixelCount];
    var palette = new short[16];
    var colorCount = 0;

    for (var i = 0; i < pixelCount; ++i) {
      var offset = i * 3;
      var r = image.PixelData[offset] * 7 / 255;
      var g = image.PixelData[offset + 1] * 7 / 255;
      var b = image.PixelData[offset + 2] * 7 / 255;
      var stColor = (short)((r << 8) | (g << 4) | b);

      var found = false;
      for (var c = 0; c < colorCount; ++c) {
        if (palette[c] != stColor)
          continue;

        chunky[i] = (byte)c;
        found = true;
        break;
      }

      if (!found) {
        if (colorCount < ColorCount) {
          palette[colorCount] = stColor;
          chunky[i] = (byte)colorCount;
          ++colorCount;
        } else
          chunky[i] = _FindClosest(stColor, palette, colorCount);
      }
    }

    var planar = PlanarConverter.ChunkyToAtariSt(chunky, ImageWidth, ImageHeight, NumPlanes);

    return new() {
      Palette1 = (short[])palette.Clone(),
      PixelData1 = (byte[])planar.Clone(),
      Palette2 = (short[])palette.Clone(),
      PixelData2 = planar,
    };
  }

  private static byte _FindClosest(short target, short[] palette, int count) {
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
