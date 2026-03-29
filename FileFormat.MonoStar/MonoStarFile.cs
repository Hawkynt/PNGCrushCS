using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MonoStar;

/// <summary>In-memory representation of a MonoStar monochrome paint image (Atari ST, 640x400, 1 bitplane).</summary>
public sealed class MonoStarFile : IImageFileFormat<MonoStarFile> {

  public const int FileSize = 32034;
  private const int _PIXEL_DATA_SIZE = 32000;
  private const int _WIDTH = 640;
  private const int _HEIGHT = 400;
  private const int _NUM_PLANES = 1;
  private const int _BYTES_PER_ROW = 80;

  static string IImageFileFormat<MonoStarFile>.PrimaryExtension => ".mst";
  static string[] IImageFileFormat<MonoStarFile>.FileExtensions => [".mst", ".mns"];
  static FormatCapability IImageFileFormat<MonoStarFile>.Capabilities => FormatCapability.MonochromeOnly;
  static MonoStarFile IImageFileFormat<MonoStarFile>.FromFile(FileInfo file) => MonoStarReader.FromFile(file);
  static MonoStarFile IImageFileFormat<MonoStarFile>.FromBytes(byte[] data) => MonoStarReader.FromBytes(data);
  static MonoStarFile IImageFileFormat<MonoStarFile>.FromStream(Stream stream) => MonoStarReader.FromStream(stream);
  static byte[] IImageFileFormat<MonoStarFile>.ToBytes(MonoStarFile file) => MonoStarWriter.ToBytes(file);

  /// <summary>Image width (always 640).</summary>
  public int Width { get; init; } = _WIDTH;

  /// <summary>Image height (always 400).</summary>
  public int Height { get; init; } = _HEIGHT;

  /// <summary>16-entry palette of 9-bit Atari ST RGB values (only first 2 entries used for mono).</summary>
  public short[] Palette { get; init; } = new short[16];

  /// <summary>32000 bytes of monochrome planar pixel data (1 bitplane, 80 bytes/row, 400 rows).</summary>
  public byte[] PixelData { get; init; } = new byte[_PIXEL_DATA_SIZE];

  public static RawImage ToRawImage(MonoStarFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var rowBytes = (_WIDTH + 7) / 8;
    var outputData = new byte[rowBytes * _HEIGHT];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, outputData.Length)).CopyTo(outputData.AsSpan(0));

    return new() {
      Width = _WIDTH,
      Height = _HEIGHT,
      Format = PixelFormat.Indexed1,
      PixelData = outputData,
      Palette = [0, 0, 0, 255, 255, 255],
      PaletteCount = 2,
    };
  }

  public static MonoStarFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed1.", nameof(image));
    if (image.Width != _WIDTH)
      throw new ArgumentException($"MonoStar images must be exactly {_WIDTH} pixels wide.", nameof(image));
    if (image.Height != _HEIGHT)
      throw new ArgumentException($"MonoStar images must be exactly {_HEIGHT} pixels tall.", nameof(image));

    var pixelData = new byte[_PIXEL_DATA_SIZE];
    var rowBytes = (_WIDTH + 7) / 8;
    var copyLen = Math.Min(rowBytes * _HEIGHT, _PIXEL_DATA_SIZE);
    image.PixelData.AsSpan(0, Math.Min(image.PixelData.Length, copyLen)).CopyTo(pixelData.AsSpan(0));

    var palette = new short[16];
    palette[0] = 0x0777; // white
    palette[1] = 0x0000; // black

    return new() {
      Width = _WIDTH,
      Height = _HEIGHT,
      PixelData = pixelData,
      Palette = palette,
    };
  }
}
