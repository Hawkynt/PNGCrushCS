using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MultiPalettePicture;

/// <summary>In-memory representation of an Atari ST Multi Palette Picture (MPP) image (320x200, 16 colors per scanline).</summary>
public sealed class MultiPalettePictureFile : IImageFileFormat<MultiPalettePictureFile> {

  /// <summary>Width is always 320 pixels.</summary>
  public const int ImageWidth = 320;

  /// <summary>Height is always 200 scanlines.</summary>
  public const int ImageHeight = 200;

  /// <summary>Number of bitplanes (low-res = 4).</summary>
  public const int NumPlanes = 4;

  /// <summary>Bytes of planar pixel data per scanline (160 bytes).</summary>
  public const int BytesPerScanline = 160;

  /// <summary>Bytes of palette data per scanline (16 words = 32 bytes).</summary>
  public const int PaletteBytesPerScanline = 32;

  /// <summary>Total bytes per scanline record (160 pixel + 32 palette = 192).</summary>
  public const int RecordSize = BytesPerScanline + PaletteBytesPerScanline;

  /// <summary>The exact file size: 200 * 192 = 38400 bytes.</summary>
  public const int ExpectedFileSize = ImageHeight * RecordSize;

  static string IImageFileFormat<MultiPalettePictureFile>.PrimaryExtension => ".mpp";
  static string[] IImageFileFormat<MultiPalettePictureFile>.FileExtensions => [".mpp"];
  static MultiPalettePictureFile IImageFileFormat<MultiPalettePictureFile>.FromFile(FileInfo file) => MultiPalettePictureReader.FromFile(file);
  static MultiPalettePictureFile IImageFileFormat<MultiPalettePictureFile>.FromBytes(byte[] data) => MultiPalettePictureReader.FromBytes(data);
  static MultiPalettePictureFile IImageFileFormat<MultiPalettePictureFile>.FromStream(Stream stream) => MultiPalettePictureReader.FromStream(stream);
  static byte[] IImageFileFormat<MultiPalettePictureFile>.ToBytes(MultiPalettePictureFile file) => MultiPalettePictureWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width { get; init; } = ImageWidth;

  /// <summary>Always 200.</summary>
  public int Height { get; init; } = ImageHeight;

  /// <summary>32000 bytes of Atari ST interleaved planar pixel data (concatenated 160-byte scanlines).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Per-scanline palettes: 200 entries, each a 16-element array of 12-bit Atari ST RGB values.</summary>
  public short[][] Palettes { get; init; } = new short[ImageHeight][];

  public static RawImage ToRawImage(MultiPalettePictureFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var chunky = PlanarConverter.AtariStToChunky(file.PixelData, ImageWidth, ImageHeight, NumPlanes);
    var rgb = new byte[ImageWidth * ImageHeight * 3];

    for (var y = 0; y < ImageHeight; ++y) {
      var palette = file.Palettes[y];
      for (var x = 0; x < ImageWidth; ++x) {
        var index = chunky[y * ImageWidth + x];
        var entry = palette[index] & 0x0FFF;
        var r = (entry >> 8) & 0x0F;
        var g = (entry >> 4) & 0x0F;
        var b = entry & 0x0F;
        var offset = (y * ImageWidth + x) * 3;
        rgb[offset] = (byte)(r * 255 / 15);
        rgb[offset + 1] = (byte)(g * 255 / 15);
        rgb[offset + 2] = (byte)(b * 255 / 15);
      }
    }

    return new() {
      Width = ImageWidth,
      Height = ImageHeight,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  public static MultiPalettePictureFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException("RawImage must use PixelFormat.Rgb24.", nameof(image));
    if (image.Width != ImageWidth)
      throw new ArgumentException($"MPP images must be exactly {ImageWidth} pixels wide.", nameof(image));
    if (image.Height != ImageHeight)
      throw new ArgumentException($"MPP images must be exactly {ImageHeight} pixels tall.", nameof(image));

    var palettes = new short[ImageHeight][];
    var chunky = new byte[ImageWidth * ImageHeight];

    for (var y = 0; y < ImageHeight; ++y) {
      var colorMap = new Dictionary<short, byte>();
      var palette = new short[16];
      var colorCount = 0;

      for (var x = 0; x < ImageWidth; ++x) {
        var offset = (y * ImageWidth + x) * 3;
        var r = image.PixelData[offset] * 15 / 255;
        var g = image.PixelData[offset + 1] * 15 / 255;
        var b = image.PixelData[offset + 2] * 15 / 255;
        var steColor = (short)((r << 8) | (g << 4) | b);

        if (!colorMap.TryGetValue(steColor, out var idx)) {
          if (colorCount < 16) {
            idx = (byte)colorCount;
            palette[colorCount] = steColor;
            colorMap[steColor] = idx;
            ++colorCount;
          } else
            idx = _FindClosestColor(steColor, palette, colorCount);
        }

        chunky[y * ImageWidth + x] = idx;
      }

      palettes[y] = palette;
    }

    var planar = PlanarConverter.ChunkyToAtariSt(chunky, ImageWidth, ImageHeight, NumPlanes);

    return new() {
      PixelData = planar,
      Palettes = palettes,
    };
  }

  private static byte _FindClosestColor(short target, short[] palette, int count) {
    var tr = (target >> 8) & 0x0F;
    var tg = (target >> 4) & 0x0F;
    var tb = target & 0x0F;
    var bestIndex = (byte)0;
    var bestDist = int.MaxValue;

    for (var i = 0; i < count; ++i) {
      var entry = palette[i];
      var dr = ((entry >> 8) & 0x0F) - tr;
      var dg = ((entry >> 4) & 0x0F) - tg;
      var db = (entry & 0x0F) - tb;
      var dist = dr * dr + dg * dg + db * db;
      if (dist >= bestDist)
        continue;

      bestDist = dist;
      bestIndex = (byte)i;
    }

    return bestIndex;
  }
}
