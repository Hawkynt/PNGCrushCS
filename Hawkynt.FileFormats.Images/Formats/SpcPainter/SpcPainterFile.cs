using System;
using FileFormat.Core;

namespace FileFormat.SpcPainter;

/// <summary>In-memory representation of an SPC Painter image (Atari ST, 320x200, 16 colors).</summary>
public readonly record struct SpcPainterFile : IImageFormatReader<SpcPainterFile>, IImageToRawImage<SpcPainterFile>, IImageFromRawImage<SpcPainterFile>, IImageFormatWriter<SpcPainterFile> {

  public const int FileSize = 32034;
  private const int _PIXEL_DATA_SIZE = 32000;
  private const int _WIDTH = 320;
  private const int _HEIGHT = 200;
  private const int _NUM_PLANES = 4;

  static string IImageFormatMetadata<SpcPainterFile>.PrimaryExtension => ".spp";
  static string[] IImageFormatMetadata<SpcPainterFile>.FileExtensions => [".spp", ".spc2"];
  static SpcPainterFile IImageFormatReader<SpcPainterFile>.FromSpan(ReadOnlySpan<byte> data) => SpcPainterReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<SpcPainterFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [new IntegerRange(2, 16)])];
  static byte[] IImageFormatWriter<SpcPainterFile>.ToBytes(SpcPainterFile file) => SpcPainterWriter.ToBytes(file);

  /// <summary>Image width (always 320).</summary>
  public int Width { get; init; }

  /// <summary>Image height (always 200).</summary>
  public int Height { get; init; }

  /// <summary>16-entry palette of 9-bit Atari ST RGB values.</summary>
  public short[] Palette { get; init; }

  /// <summary>32000 bytes of Atari ST interleaved planar pixel data.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(SpcPainterFile file) {

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


  /// <summary>Encodes a picture as an SPC Painter picture, scaling it to 320x200 first.</summary>
  /// <remarks>
  /// An Atari ST low-resolution screen: sixteen colours, four bitplanes interleaved a word at a
  /// time, and a palette of nine-bit values. The palette is built from the picture rather than fixed
  /// by the machine, so the colours are quantised first and the indices then split into planes —
  /// the exact inverse of what <see cref="ToRawImage"/> puts back together.
  /// </remarks>
  public static SpcPainterFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var indexed = image.SampleTo(320, 200).EnsureFormat(PixelFormat.Indexed8);
    var quantised = ColorQuantizer.Quantize(
      PixelConverter.Convert(indexed, PixelFormat.Bgra32).PixelData, 320 * 200, 16);

    var chunky = new byte[320 * 200];
    for (var i = 0; i < chunky.Length; ++i)
      chunky[i] = (byte)quantised.Indices[i];

    var palette = new short[16];
    PlanarConverter.RgbToStPalette(quantised.Palette, quantised.Count).AsSpan(0, Math.Min(quantised.Count, 16)).CopyTo(palette);

    return new() {
      Width = 320,
      Height = 200,
      Palette = palette,
      PixelData = PlanarConverter.ChunkyToAtariSt(chunky, 320, 200, 4),
    };
  }

}
