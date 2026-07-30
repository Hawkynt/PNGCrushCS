using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Spectrum512Ext;

/// <summary>In-memory representation of a Spectrum 512 Extended (.spx) image (Atari ST, 320x199, up to 4096 colors).</summary>
public readonly record struct Spectrum512ExtFile : IImageFormatReader<Spectrum512ExtFile>, IImageToRawImage<Spectrum512ExtFile>, IImageFromRawImage<Spectrum512ExtFile>, IImageFormatWriter<Spectrum512ExtFile> {

  /// <summary>Expected file size: 32000 bytes pixel data + 199 lines * 48 entries * 2 bytes = 51104 bytes.</summary>
  /// <summary>Bytes of Atari ST interleaved planar pixel data.</summary>
  public const int PixelDataSize = 32000;

  /// <summary>ASCII signature every SPX file starts with.</summary>
  public static System.ReadOnlySpan<byte> Signature => "SPX"u8;

  /// <summary>Offset of the two zero bytes that terminate the header's title field. Readers scan
  /// from offset 10 until they have consumed two of them, which is what fixes the data offset.</summary>
  public const int TitleTerminatorOffset = 10;

  /// <summary>Offset of the big-endian bitmap and palette length fields.</summary>
  public const int LengthFieldsOffset = 12;

  /// <summary>Offset of the bitmap block.</summary>
  public const int BitmapOffset = LengthFieldsOffset + 8;

  /// <summary>The bitmap block opens with one unused 160-byte scanline before the picture.</summary>
  public const int BitmapLeadIn = 160;

  /// <summary>Offset of the per-scanline palette block.</summary>
  public const int PaletteOffset = BitmapOffset + PixelDataSize;

  /// <summary>Total palette size: 48 big-endian entries for each scanline.</summary>
  public const int PaletteDataSize = ScanlineCount * PaletteEntriesPerLine * 2;

  public const int FileSize = PaletteOffset + PaletteDataSize;

  /// <summary>Number of scanlines.</summary>
  public const int ScanlineCount = 199;

  /// <summary>Palette entries per scanline.</summary>
  public const int PaletteEntriesPerLine = 48;

  static string IImageFormatMetadata<Spectrum512ExtFile>.PrimaryExtension => ".spx";
  static string[] IImageFormatMetadata<Spectrum512ExtFile>.FileExtensions => [".spx"];
  static Spectrum512ExtFile IImageFormatReader<Spectrum512ExtFile>.FromSpan(ReadOnlySpan<byte> data) => Spectrum512ExtReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<Spectrum512ExtFile>.VideoModes => [new("Default", [(320, ScanlineCount)])];
  static byte[] IImageFormatWriter<Spectrum512ExtFile>.ToBytes(Spectrum512ExtFile file) => Spectrum512ExtWriter.ToBytes(file);

  /// <summary>Image width (always 320).</summary>
  public int Width { get; init; }

  /// <summary>Image height (always 199).</summary>
  public int Height { get; init; }

  /// <summary>32000 bytes of Atari ST interleaved planar pixel data (4 planes).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Per-scanline palettes: 199 lines, each with 48 palette entries (3 entries per 16-pixel group).</summary>
  public short[][] Palettes { get; init; }

  public static RawImage ToRawImage(Spectrum512ExtFile file) {

    const int width = 320;
    var height = file.Height;
    var chunky = PlanarConverter.AtariStToChunky(file.PixelData, width, height, 4);
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var palette = file.Palettes[y];
      for (var x = 0; x < width; ++x) {
        var entry = palette[FileFormat.Spectrum512.Spectrum512File.PaletteEntryFor(chunky[y * width + x], x)];
        var color = AtariStGraphics.InterlacedColorToRgb(entry & 0xFFFF);
        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)(color >> 16);
        rgb[offset + 1] = (byte)(color >> 8);
        rgb[offset + 2] = (byte)color;
      }
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  public static Spectrum512ExtFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      image = PixelConverter.Convert(image, PixelFormat.Rgb24);
    if (image.Width != 320)
      throw new ArgumentException("Spectrum 512 Extended images must be exactly 320 pixels wide.", nameof(image));
    if (image.Height != ScanlineCount)
      throw new ArgumentException($"Spectrum 512 Extended images must be exactly {ScanlineCount} pixels tall.", nameof(image));

    const int width = 320;
    var palettes = new short[ScanlineCount][];
    var chunky = new byte[width * ScanlineCount];

    for (var y = 0; y < ScanlineCount; ++y) {
      var colorMap = new Dictionary<short, byte>();
      var palette = new short[16];
      var colorCount = 0;

      for (var x = 0; x < width; ++x) {
        var offset = (y * width + x) * 3;
        // Five bits a channel, in the scattered form the interlaced word keeps them.
        var r = image.PixelData[offset] * 31 / 255;
        var g = image.PixelData[offset + 1] * 31 / 255;
        var b = image.PixelData[offset + 2] * 31 / 255;
        var stColor = (short)AtariStGraphics.RgbToInterlacedColor(r, g, b);

        if (!colorMap.TryGetValue(stColor, out var index)) {
          if (colorCount < 16) {
            index = (byte)colorCount;
            palette[colorCount] = stColor;
            colorMap[stColor] = index;
            ++colorCount;
          } else
            index = _FindClosestColor(stColor, palette, colorCount);
        }

        chunky[y * width + x] = index;
      }

      // Build 48-entry palette (3 entries per 16-pixel group)
      var extPalette = new short[PaletteEntriesPerLine];
      for (var i = 0; i < PaletteEntriesPerLine && i < palette.Length * 3; ++i)
        extPalette[i] = palette[i % 16];

      palettes[y] = extPalette;
    }

    var planar = PlanarConverter.ChunkyToAtariSt(chunky, width, ScanlineCount, 4);

    return new() {
      Width = width,
      Height = ScanlineCount,
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
