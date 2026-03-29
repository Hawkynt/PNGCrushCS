using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Spectrum512Comp;

/// <summary>In-memory representation of an Atari ST Spectrum 512 Compressed (SPC) image (320x199, 512 colors).</summary>
public sealed class Spectrum512CompFile : IImageFileFormat<Spectrum512CompFile> {

  /// <summary>The decompressed data size (same as SPU format): 32000 + 19104 = 51104 bytes.</summary>
  public const int DecompressedSize = 51104;

  /// <summary>Minimum file size for validation.</summary>
  public const int MinFileSize = 4;

  private const int _PIXEL_DATA_SIZE = 32000;
  private const int _SCANLINE_COUNT = 199;
  private const int _PALETTE_ENTRIES_PER_LINE = 48;

  static string IImageFileFormat<Spectrum512CompFile>.PrimaryExtension => ".spc";
  static string[] IImageFileFormat<Spectrum512CompFile>.FileExtensions => [".spc"];
  static Spectrum512CompFile IImageFileFormat<Spectrum512CompFile>.FromFile(FileInfo file) => Spectrum512CompReader.FromFile(file);
  static Spectrum512CompFile IImageFileFormat<Spectrum512CompFile>.FromBytes(byte[] data) => Spectrum512CompReader.FromBytes(data);
  static Spectrum512CompFile IImageFileFormat<Spectrum512CompFile>.FromStream(Stream stream) => Spectrum512CompReader.FromStream(stream);
  static byte[] IImageFileFormat<Spectrum512CompFile>.ToBytes(Spectrum512CompFile file) => Spectrum512CompWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => 320;

  /// <summary>Always 199.</summary>
  public int Height => 199;

  /// <summary>The raw compressed data bytes.</summary>
  public byte[] RawData { get; init; } = [];

  public static RawImage ToRawImage(Spectrum512CompFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var decompressed = _PackBitsDecompress(file.RawData, DecompressedSize);
    if (decompressed.Length < DecompressedSize)
      throw new InvalidDataException($"Decompressed data too small: expected {DecompressedSize} bytes, got {decompressed.Length}.");

    const int width = 320;
    const int height = _SCANLINE_COUNT;
    var pixelData = new byte[_PIXEL_DATA_SIZE];
    decompressed.AsSpan(0, _PIXEL_DATA_SIZE).CopyTo(pixelData.AsSpan(0));

    var chunky = PlanarConverter.AtariStToChunky(pixelData, width, height, 4);
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var paletteOffset = _PIXEL_DATA_SIZE + y * _PALETTE_ENTRIES_PER_LINE * 2;
      var palette = new short[_PALETTE_ENTRIES_PER_LINE];
      for (var e = 0; e < _PALETTE_ENTRIES_PER_LINE; ++e)
        palette[e] = BinaryPrimitives.ReadInt16BigEndian(decompressed.AsSpan(paletteOffset + e * 2));

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

  public static Spectrum512CompFile FromRawImage(RawImage image) => throw new NotSupportedException("Spectrum512Comp format does not support creation from RawImage.");

  private static byte[] _PackBitsDecompress(byte[] compressed, int expectedSize) {
    var result = new byte[expectedSize];
    var srcPos = 0;
    var dstPos = 0;

    while (srcPos < compressed.Length && dstPos < expectedSize) {
      var n = (sbyte)compressed[srcPos++];
      if (n >= 0) {
        var count = n + 1;
        for (var i = 0; i < count && srcPos < compressed.Length && dstPos < expectedSize; ++i)
          result[dstPos++] = compressed[srcPos++];
      } else if (n != -128) {
        var count = 1 - n;
        if (srcPos >= compressed.Length)
          break;
        var value = compressed[srcPos++];
        for (var i = 0; i < count && dstPos < expectedSize; ++i)
          result[dstPos++] = value;
      }
    }

    return result;
  }
}
