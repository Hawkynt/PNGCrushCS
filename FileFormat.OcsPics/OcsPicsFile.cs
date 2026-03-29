using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.OcsPics;

/// <summary>In-memory representation of an OCS Pics image (Atari ST, 320x200, 16 colors).</summary>
public sealed class OcsPicsFile : IImageFileFormat<OcsPicsFile> {

  public const int FileSize = 32034;
  private const int _PIXEL_DATA_SIZE = 32000;
  private const int _WIDTH = 320;
  private const int _HEIGHT = 200;
  private const int _NUM_PLANES = 4;

  static string IImageFileFormat<OcsPicsFile>.PrimaryExtension => ".ocp";
  static string[] IImageFileFormat<OcsPicsFile>.FileExtensions => [".ocp", ".ocs"];
  static FormatCapability IImageFileFormat<OcsPicsFile>.Capabilities => FormatCapability.IndexedOnly;
  static OcsPicsFile IImageFileFormat<OcsPicsFile>.FromFile(FileInfo file) => OcsPicsReader.FromFile(file);
  static OcsPicsFile IImageFileFormat<OcsPicsFile>.FromBytes(byte[] data) => OcsPicsReader.FromBytes(data);
  static OcsPicsFile IImageFileFormat<OcsPicsFile>.FromStream(Stream stream) => OcsPicsReader.FromStream(stream);
  static byte[] IImageFileFormat<OcsPicsFile>.ToBytes(OcsPicsFile file) => OcsPicsWriter.ToBytes(file);

  /// <summary>Image width (always 320).</summary>
  public int Width { get; init; } = _WIDTH;

  /// <summary>Image height (always 200).</summary>
  public int Height { get; init; } = _HEIGHT;

  /// <summary>16-entry palette of 9-bit Atari ST RGB values.</summary>
  public short[] Palette { get; init; } = new short[16];

  /// <summary>32000 bytes of Atari ST interleaved planar pixel data.</summary>
  public byte[] PixelData { get; init; } = new byte[_PIXEL_DATA_SIZE];

  public static RawImage ToRawImage(OcsPicsFile file) {
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

  public static OcsPicsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to OCS Pics is not supported.");
  }
}
