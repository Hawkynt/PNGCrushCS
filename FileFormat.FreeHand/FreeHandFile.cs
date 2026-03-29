using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FreeHand;

/// <summary>In-memory representation of a FreeHand ST bitmap export image (Atari ST, 320x200, 16 colors).</summary>
public sealed class FreeHandFile : IImageFileFormat<FreeHandFile> {

  public const int FileSize = 32034;
  private const int _PIXEL_DATA_SIZE = 32000;
  private const int _WIDTH = 320;
  private const int _HEIGHT = 200;
  private const int _NUM_PLANES = 4;

  static string IImageFileFormat<FreeHandFile>.PrimaryExtension => ".fhs";
  static string[] IImageFileFormat<FreeHandFile>.FileExtensions => [".fhs"];
  static FormatCapability IImageFileFormat<FreeHandFile>.Capabilities => FormatCapability.IndexedOnly;
  static FreeHandFile IImageFileFormat<FreeHandFile>.FromFile(FileInfo file) => FreeHandReader.FromFile(file);
  static FreeHandFile IImageFileFormat<FreeHandFile>.FromBytes(byte[] data) => FreeHandReader.FromBytes(data);
  static FreeHandFile IImageFileFormat<FreeHandFile>.FromStream(Stream stream) => FreeHandReader.FromStream(stream);
  static byte[] IImageFileFormat<FreeHandFile>.ToBytes(FreeHandFile file) => FreeHandWriter.ToBytes(file);

  /// <summary>Image width (always 320).</summary>
  public int Width { get; init; } = _WIDTH;

  /// <summary>Image height (always 200).</summary>
  public int Height { get; init; } = _HEIGHT;

  /// <summary>16-entry palette of 9-bit Atari ST RGB values.</summary>
  public short[] Palette { get; init; } = new short[16];

  /// <summary>32000 bytes of Atari ST interleaved planar pixel data.</summary>
  public byte[] PixelData { get; init; } = new byte[_PIXEL_DATA_SIZE];

  public static RawImage ToRawImage(FreeHandFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var chunky = PlanarConverter.AtariStToChunky(file.PixelData, _WIDTH, _HEIGHT, _NUM_PLANES);
    var paletteCount = Math.Min(16, file.Palette.Length);
    var rgb = PlanarConverter.StPaletteToRgb(file.Palette.AsSpan(0, paletteCount));

    return new() {
      Width = _WIDTH,
      Height = _HEIGHT,
      Format = PixelFormat.Indexed8,
      PixelData = chunky,
      Palette = rgb,
      PaletteCount = paletteCount,
    };
  }

  public static FreeHandFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to FreeHand ST is not supported.");
  }
}
