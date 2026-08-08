using System;
using FileFormat.Core;

namespace FileFormat.FreeHand;

/// <summary>In-memory representation of a FreeHand ST bitmap export image (Atari ST, 320x200, 16 colors).</summary>
public readonly record struct FreeHandFile : IImageFormatReader<FreeHandFile>, IImageToRawImage<FreeHandFile>, IImageFromRawImage<FreeHandFile>, IImageFormatWriter<FreeHandFile> {

  public const int FileSize = 32034;
  private const int _PIXEL_DATA_SIZE = 32000;
  private const int _WIDTH = 320;
  private const int _HEIGHT = 200;
  private const int _NUM_PLANES = 4;

  static string IImageFormatMetadata<FreeHandFile>.PrimaryExtension => ".fhs";
  static string[] IImageFormatMetadata<FreeHandFile>.FileExtensions => [".fhs"];
  static FreeHandFile IImageFormatReader<FreeHandFile>.FromSpan(ReadOnlySpan<byte> data) => FreeHandReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<FreeHandFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [new IntegerRange(2, 16)])
  ];
  static byte[] IImageFormatWriter<FreeHandFile>.ToBytes(FreeHandFile file) => FreeHandWriter.ToBytes(file);

  /// <summary>Image width (always 320).</summary>
  public int Width { get; init; }

  /// <summary>Image height (always 200).</summary>
  public int Height { get; init; }

  /// <summary>16-entry palette of 9-bit Atari ST RGB values.</summary>
  public short[] Palette { get; init; }

  /// <summary>32000 bytes of Atari ST interleaved planar pixel data.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(FreeHandFile file) {

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

  /// <summary>Creates a FreeHand ST export from a <see cref="RawImage"/>, sampling it to the ST's 320x200 low-resolution screen.</summary>
  /// <remarks>
  /// The file is a fixed 32034 bytes with no field for a size, so a picture of any other size is
  /// sampled to the screen rather than refused. The sixteen colours are picked by median cut and
  /// then snapped to the ST's three-bits-per-channel palette, and the body is word-interleaved
  /// across four bitplanes — the exact inverse of what <see cref="ToRawImage"/> unpicks.
  /// </remarks>
  public static FreeHandFile FromRawImage(RawImage image) {
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
