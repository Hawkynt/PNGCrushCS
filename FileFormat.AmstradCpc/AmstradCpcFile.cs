using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AmstradCpc;

/// <summary>In-memory representation of an Amstrad CPC screen memory dump.</summary>
public sealed class AmstradCpcFile : IImageFileFormat<AmstradCpcFile> {

  static string IImageFileFormat<AmstradCpcFile>.PrimaryExtension => ".cpc";
  static string[] IImageFileFormat<AmstradCpcFile>.FileExtensions => [".cpc"];
  static AmstradCpcFile IImageFileFormat<AmstradCpcFile>.FromFile(FileInfo file) => AmstradCpcReader.FromFile(file);
  static AmstradCpcFile IImageFileFormat<AmstradCpcFile>.FromBytes(byte[] data) => AmstradCpcReader.FromBytes(data);
  static AmstradCpcFile IImageFileFormat<AmstradCpcFile>.FromStream(Stream stream) => AmstradCpcReader.FromStream(stream);
  static byte[] IImageFileFormat<AmstradCpcFile>.ToBytes(AmstradCpcFile file) => AmstradCpcWriter.ToBytes(file);
  /// <summary>Width in pixels (depends on mode: 160, 320, or 640).</summary>
  public int Width { get; init; }
  /// <summary>Height in pixels (always 200).</summary>
  public int Height { get; init; }
  /// <summary>Screen mode.</summary>
  public AmstradCpcMode Mode { get; init; }
  /// <summary>Raw screen memory (16384 bytes for standard screen).</summary>
  public byte[] PixelData { get; init; } = [];
  /// <summary>Hardware palette indices (up to 16 entries), optional.</summary>
  public byte[]? Palette { get; init; }

  /// <summary>Bytes per scanline in all modes.</summary>
  private const int _BYTES_PER_LINE = 80;

  /// <summary>Fixed screen height.</summary>
  private const int _FIXED_HEIGHT = 200;

  /// <summary>Gets the number of pixels per packed byte for a given mode.</summary>
  private static int _GetPixelsPerByte(AmstradCpcMode mode) => mode switch {
    AmstradCpcMode.Mode0 => 2,
    AmstradCpcMode.Mode1 => 4,
    AmstradCpcMode.Mode2 => 8,
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown CPC mode.")
  };

  /// <summary>Gets the palette entry count for a given mode.</summary>
  private static int _GetPaletteCount(AmstradCpcMode mode) => mode switch {
    AmstradCpcMode.Mode0 => 16,
    AmstradCpcMode.Mode1 => 4,
    AmstradCpcMode.Mode2 => 2,
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown CPC mode.")
  };

  /// <summary>Gets the expected pixel width for a given mode.</summary>
  private static int _GetWidth(AmstradCpcMode mode) => mode switch {
    AmstradCpcMode.Mode0 => 160,
    AmstradCpcMode.Mode1 => 320,
    AmstradCpcMode.Mode2 => 640,
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown CPC mode.")
  };

  /// <summary>Determines the CPC mode from pixel width.</summary>
  private static AmstradCpcMode _ModeFromWidth(int width) => width switch {
    160 => AmstradCpcMode.Mode0,
    320 => AmstradCpcMode.Mode1,
    640 => AmstradCpcMode.Mode2,
    _ => throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be 160, 320, or 640.")
  };

  /// <summary>Converts this Amstrad CPC screen to a platform-independent <see cref="RawImage"/> in Indexed8 format.</summary>
  public static RawImage ToRawImage(AmstradCpcFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var width = file.Width;
    var height = file.Height;
    var pixelsPerByte = _GetPixelsPerByte(file.Mode);
    var pixels = new byte[width * height];

    for (var y = 0; y < height; ++y)
      for (var byteCol = 0; byteCol < _BYTES_PER_LINE; ++byteCol) {
        var srcOffset = y * _BYTES_PER_LINE + byteCol;
        if (srcOffset >= file.PixelData.Length)
          continue;

        var unpacked = AmstradCpcPixelPacker.UnpackByte(file.PixelData[srcOffset], file.Mode);
        var baseX = byteCol * pixelsPerByte;
        for (var px = 0; px < unpacked.Length; ++px) {
          var x = baseX + px;
          if (x < width)
            pixels[y * width + x] = unpacked[px];
        }
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      PaletteCount = _GetPaletteCount(file.Mode),
    };
  }

  /// <summary>Creates an Amstrad CPC screen from a platform-independent <see cref="RawImage"/>.</summary>
  public static AmstradCpcFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("RawImage must use Indexed8 pixel format.", nameof(image));

    if (image.Height != _FIXED_HEIGHT)
      throw new ArgumentException($"Height must be {_FIXED_HEIGHT}.", nameof(image));

    var mode = _ModeFromWidth(image.Width);
    var pixelsPerByte = _GetPixelsPerByte(mode);
    var width = image.Width;
    var packedData = new byte[_BYTES_PER_LINE * _FIXED_HEIGHT];

    for (var y = 0; y < _FIXED_HEIGHT; ++y)
      for (var byteCol = 0; byteCol < _BYTES_PER_LINE; ++byteCol) {
        var chunk = new byte[pixelsPerByte];
        var baseX = byteCol * pixelsPerByte;
        for (var px = 0; px < pixelsPerByte; ++px) {
          var x = baseX + px;
          chunk[px] = x < width ? image.PixelData[y * width + x] : (byte)0;
        }

        packedData[y * _BYTES_PER_LINE + byteCol] = AmstradCpcPixelPacker.PackByte(chunk, mode);
      }

    return new() {
      Width = width,
      Height = _FIXED_HEIGHT,
      Mode = mode,
      PixelData = packedData,
    };
  }
}
