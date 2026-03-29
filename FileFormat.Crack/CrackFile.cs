using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Crack;

/// <summary>In-memory representation of a Crack Art 2 image (Atari ST, 320x200, 16 colors).</summary>
public sealed class CrackFile : IImageFileFormat<CrackFile> {

  public const int FileSize = 32034;
  private const int _PIXEL_DATA_SIZE = 32000;
  private const int _WIDTH = 320;
  private const int _HEIGHT = 200;
  private const int _NUM_PLANES = 4;

  static string IImageFileFormat<CrackFile>.PrimaryExtension => ".ca2";
  static string[] IImageFileFormat<CrackFile>.FileExtensions => [".ca2"];
  static FormatCapability IImageFileFormat<CrackFile>.Capabilities => FormatCapability.IndexedOnly;
  static CrackFile IImageFileFormat<CrackFile>.FromFile(FileInfo file) => CrackReader.FromFile(file);
  static CrackFile IImageFileFormat<CrackFile>.FromBytes(byte[] data) => CrackReader.FromBytes(data);
  static CrackFile IImageFileFormat<CrackFile>.FromStream(Stream stream) => CrackReader.FromStream(stream);
  static byte[] IImageFileFormat<CrackFile>.ToBytes(CrackFile file) => CrackWriter.ToBytes(file);

  /// <summary>Image width (always 320).</summary>
  public int Width { get; init; } = _WIDTH;

  /// <summary>Image height (always 200).</summary>
  public int Height { get; init; } = _HEIGHT;

  /// <summary>16-entry palette of 9-bit Atari ST RGB values.</summary>
  public short[] Palette { get; init; } = new short[16];

  /// <summary>32000 bytes of Atari ST interleaved planar pixel data.</summary>
  public byte[] PixelData { get; init; } = new byte[_PIXEL_DATA_SIZE];

  public static RawImage ToRawImage(CrackFile file) {
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

  public static CrackFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to Crack Art 2 is not supported.");
  }
}
