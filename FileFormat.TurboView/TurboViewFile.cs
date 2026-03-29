using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.TurboView;

/// <summary>In-memory representation of a Turbo View image (Atari ST, 320x200, 16 colors).</summary>
public sealed class TurboViewFile : IImageFileFormat<TurboViewFile> {

  public const int FileSize = 32034;
  private const int _PIXEL_DATA_SIZE = 32000;
  private const int _WIDTH = 320;
  private const int _HEIGHT = 200;
  private const int _NUM_PLANES = 4;

  static string IImageFileFormat<TurboViewFile>.PrimaryExtension => ".tvw";
  static string[] IImageFileFormat<TurboViewFile>.FileExtensions => [".tvw", ".tbv"];
  static FormatCapability IImageFileFormat<TurboViewFile>.Capabilities => FormatCapability.IndexedOnly;
  static TurboViewFile IImageFileFormat<TurboViewFile>.FromFile(FileInfo file) => TurboViewReader.FromFile(file);
  static TurboViewFile IImageFileFormat<TurboViewFile>.FromBytes(byte[] data) => TurboViewReader.FromBytes(data);
  static TurboViewFile IImageFileFormat<TurboViewFile>.FromStream(Stream stream) => TurboViewReader.FromStream(stream);
  static byte[] IImageFileFormat<TurboViewFile>.ToBytes(TurboViewFile file) => TurboViewWriter.ToBytes(file);

  /// <summary>Image width (always 320).</summary>
  public int Width { get; init; } = _WIDTH;

  /// <summary>Image height (always 200).</summary>
  public int Height { get; init; } = _HEIGHT;

  /// <summary>16-entry palette of 9-bit Atari ST RGB values.</summary>
  public short[] Palette { get; init; } = new short[16];

  /// <summary>32000 bytes of Atari ST interleaved planar pixel data.</summary>
  public byte[] PixelData { get; init; } = new byte[_PIXEL_DATA_SIZE];

  public static RawImage ToRawImage(TurboViewFile file) {
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

  public static TurboViewFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to Turbo View is not supported.");
  }
}
