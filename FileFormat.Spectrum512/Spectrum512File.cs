using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Spectrum512;

/// <summary>In-memory representation of a Spectrum 512 image (Atari ST 512-color).</summary>
public sealed class Spectrum512File : IImageFileFormat<Spectrum512File> {

  static string IImageFileFormat<Spectrum512File>.PrimaryExtension => ".spu";
  static string[] IImageFileFormat<Spectrum512File>.FileExtensions => [".spu"];
  static Spectrum512File IImageFileFormat<Spectrum512File>.FromFile(FileInfo file) => Spectrum512Reader.FromFile(file);
  static Spectrum512File IImageFileFormat<Spectrum512File>.FromBytes(byte[] data) => Spectrum512Reader.FromBytes(data);
  static Spectrum512File IImageFileFormat<Spectrum512File>.FromStream(Stream stream) => Spectrum512Reader.FromStream(stream);
  static byte[] IImageFileFormat<Spectrum512File>.ToBytes(Spectrum512File file) => Spectrum512Writer.ToBytes(file);
  public int Width { get; init; } = 320;
  public int Height { get; init; } = 199;
  public Spectrum512Variant Variant { get; init; }
  public byte[] PixelData { get; init; } = new byte[32000];
  public short[][] Palettes { get; init; } = new short[199][];

  public static RawImage ToRawImage(Spectrum512File file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = 320;
    var height = file.Height;
    var chunky = PlanarConverter.AtariStToChunky(file.PixelData, width, height, 4);
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var palette = file.Palettes[y];
      for (var x = 0; x < width; ++x) {
        var index = chunky[y * width + x];
        var entry = palette[index] & 0x0FFF;
        var r = (entry >> 8) & 0x07;
        var g = (entry >> 4) & 0x07;
        var b = entry & 0x07;
        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)(r * 255 / 7);
        rgb[offset + 1] = (byte)(g * 255 / 7);
        rgb[offset + 2] = (byte)(b * 255 / 7);
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
      throw new ArgumentException("RawImage must use PixelFormat.Rgb24.", nameof(image));
    if (image.Width != 320)
      throw new ArgumentException("Spectrum 512 images must be exactly 320 pixels wide.", nameof(image));
    if (image.Height != 199)
      throw new ArgumentException("Spectrum 512 images must be exactly 199 pixels tall.", nameof(image));

    const int width = 320;
    const int height = 199;
    var palettes = new short[height][];
    var chunky = new byte[width * height];

    for (var y = 0; y < height; ++y) {
      var colorMap = new Dictionary<short, byte>();
      var palette = new short[16];
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
