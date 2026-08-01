using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.MultiPalettePicture;

/// <summary>In-memory representation of an Atari ST Multi Palette Picture (MPP) image (320x200, 16 colors per scanline).</summary>
public readonly record struct MultiPalettePictureFile : IImageFormatReader<MultiPalettePictureFile>, IImageToRawImage<MultiPalettePictureFile>, IImageFromRawImage<MultiPalettePictureFile>, IImageFormatWriter<MultiPalettePictureFile> {

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

  static string IImageFormatMetadata<MultiPalettePictureFile>.PrimaryExtension => ".mpp";
  static string[] IImageFormatMetadata<MultiPalettePictureFile>.FileExtensions => [".mpp"];
  static MultiPalettePictureFile IImageFormatReader<MultiPalettePictureFile>.FromSpan(ReadOnlySpan<byte> data) => MultiPalettePictureReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<MultiPalettePictureFile>.VideoModes => [new("Default", [(ImageWidth, ImageHeight)])];
  static byte[] IImageFormatWriter<MultiPalettePictureFile>.ToBytes(MultiPalettePictureFile file) => MultiPalettePictureWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width { get; init; }

  /// <summary>Always 200.</summary>
  public int Height { get; init; }

  /// <summary>32000 bytes of Atari ST interleaved planar pixel data (concatenated 160-byte scanlines).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Per-scanline palettes: 200 entries, each a 16-element array of 12-bit Atari ST RGB values.</summary>
  public short[][] Palettes { get; init; }

  public static RawImage ToRawImage(MultiPalettePictureFile file) {

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
    // Converted rather than refused. Every other writer here takes whatever picture it is
    // handed and does what the format needs; one that insists the caller reduce first pushes
    // that work onto anything converting between formats, which is most of what this is for.
    image = image.EnsureAnyFormat(PixelFormat.Rgb24, PixelFormat.Indexed8, PixelFormat.Indexed1);
    if (image.Width != ImageWidth)
      throw new ArgumentException($"MPP images must be exactly {ImageWidth} pixels wide.", nameof(image));
    if (image.Height != ImageHeight)
      throw new ArgumentException($"MPP images must be exactly {ImageHeight} pixels tall.", nameof(image));

    var palettes = new short[ImageHeight][];
    var chunky = new byte[ImageWidth * ImageHeight];

    if (image.Format == PixelFormat.Indexed1 || image.Format == PixelFormat.Indexed8) {
      var palette = image.Palette ?? throw new ArgumentException("Indexed input requires a palette.", nameof(image));
      var paletteCount = Math.Min(image.PaletteCount, 16);
      var stPalette = new short[16];
      for (var i = 0; i < paletteCount; ++i) {
        var r = palette[i * 3] * 15 / 255;
        var g = palette[i * 3 + 1] * 15 / 255;
        var b = palette[i * 3 + 2] * 15 / 255;
        stPalette[i] = (short)((r << 8) | (g << 4) | b);
      }

      if (image.Format == PixelFormat.Indexed1) {
        var stride = (ImageWidth + 7) / 8;
        for (var y = 0; y < ImageHeight; ++y) {
          for (var x = 0; x < ImageWidth; ++x) {
            var b = image.PixelData[y * stride + (x >> 3)];
            chunky[y * ImageWidth + x] = (byte)((b >> (7 - (x & 7))) & 1);
          }
          palettes[y] = (short[])stPalette.Clone();
        }
      } else {
        for (var y = 0; y < ImageHeight; ++y) {
          for (var x = 0; x < ImageWidth; ++x)
            chunky[y * ImageWidth + x] = (byte)(image.PixelData[y * ImageWidth + x] & 0x0F);
          palettes[y] = (short[])stPalette.Clone();
        }
      }
    } else if (image.Format == PixelFormat.Rgb24) {
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
              throw new ArgumentException($"MPP requires no more than 16 distinct quantized colours per scanline; line {y} had more.", nameof(image));
          }

          chunky[y * ImageWidth + x] = idx;
        }

        palettes[y] = palette;
      }
    } else {
      throw new ArgumentException("RawImage must use PixelFormat.Rgb24, Indexed1, or Indexed8.", nameof(image));
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
