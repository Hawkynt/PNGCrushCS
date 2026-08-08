using System;
using FileFormat.Core;

namespace FileFormat.OcsPics;

/// <summary>In-memory representation of an OCS Pics image (Atari ST, 320x200, 16 colors).</summary>
public readonly record struct OcsPicsFile : IImageFormatReader<OcsPicsFile>, IImageToRawImage<OcsPicsFile>, IImageFromRawImage<OcsPicsFile>, IImageFormatWriter<OcsPicsFile> {

  public const int FileSize = 32034;
  private const int _PIXEL_DATA_SIZE = 32000;
  private const int _WIDTH = 320;
  private const int _HEIGHT = 200;
  private const int _NUM_PLANES = 4;

  static string IImageFormatMetadata<OcsPicsFile>.PrimaryExtension => ".ocp";
  static string[] IImageFormatMetadata<OcsPicsFile>.FileExtensions => [".ocp", ".ocs"];
  static OcsPicsFile IImageFormatReader<OcsPicsFile>.FromSpan(ReadOnlySpan<byte> data) => OcsPicsReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<OcsPicsFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [new IntegerRange(2, 16)])];
  static byte[] IImageFormatWriter<OcsPicsFile>.ToBytes(OcsPicsFile file) => OcsPicsWriter.ToBytes(file);

  /// <summary>Image width (always 320).</summary>
  public int Width { get; init; }

  /// <summary>Image height (always 200).</summary>
  public int Height { get; init; }

  /// <summary>16-entry palette of 9-bit Atari ST RGB values.</summary>
  public short[] Palette { get; init; }

  /// <summary>32000 bytes of Atari ST interleaved planar pixel data.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(OcsPicsFile file) {

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

  /// <summary>Creates an OCS Pics image from a <see cref="RawImage"/>, sampling it to the ST's 320x200 low-resolution screen.</summary>
  /// <remarks>
  /// The file is a fixed 32034 bytes with no field for a size, so a picture of any other size is
  /// sampled to the screen rather than refused. The sixteen colours are picked by median cut and
  /// then snapped to the ST's three-bits-per-channel palette, and the body is word-interleaved
  /// across four bitplanes — the exact inverse of what <see cref="ToRawImage"/> unpicks.
  /// </remarks>
  public static OcsPicsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var sampled = image.SampleTo(_WIDTH, _HEIGHT);
    var quantized = ColorQuantizer.Quantize(sampled.ToBgra32(), _WIDTH * _HEIGHT, 16);

    var chunky = new byte[_WIDTH * _HEIGHT];
    for (var i = 0; i < chunky.Length; ++i)
      chunky[i] = (byte)quantized.Indices[i];

    var palette = new short[16];
    PlanarConverter.RgbToStPalette(quantized.Palette, quantized.Count).AsSpan().CopyTo(palette);

    return new() {
      Width = _WIDTH,
      Height = _HEIGHT,
      Palette = palette,
      PixelData = PlanarConverter.ChunkyToAtariSt(chunky, _WIDTH, _HEIGHT, _NUM_PLANES),
    };
  }

}
