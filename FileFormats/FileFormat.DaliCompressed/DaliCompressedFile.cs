using System;
using System.Globalization;
using System.Text;
using FileFormat.Core;

namespace FileFormat.DaliCompressed;

/// <summary>In-memory representation of a compressed Atari ST Dali screen.</summary>
/// <remarks>
/// A 32-byte palette, then two lengths written as ASCII decimal each followed by CR LF, then the
/// run-count stream and the value stream back to back. Writing the lengths as text rather than
/// binary is unusual but it is what the format does, and readers parse them that way.
/// </remarks>
public readonly record struct DaliCompressedFile
  : IImageFormatReader<DaliCompressedFile>, IImageToRawImage<DaliCompressedFile>,
    IImageFromRawImage<DaliCompressedFile>, IImageFormatWriter<DaliCompressedFile> {

  /// <summary>Size of the palette block.</summary>
  public const int PaletteSize = 32;

  /// <summary>Offset of the first ASCII length.</summary>
  public const int LengthsOffset = PaletteSize;

  static string IImageFormatMetadata<DaliCompressedFile>.PrimaryExtension => ".lpk";
  static string[] IImageFormatMetadata<DaliCompressedFile>.FileExtensions => [".lpk", ".mpk", ".hpk"];
  static DaliCompressedFile IImageFormatReader<DaliCompressedFile>.FromSpan(ReadOnlySpan<byte> data)
    => DaliCompressedReader.FromSpan(data);
  static byte[] IImageFormatWriter<DaliCompressedFile>.ToBytes(DaliCompressedFile file)
    => DaliCompressedWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<DaliCompressedFile>.VideoModes => [
    new("Low resolution", [(320, 200)], [16]),
    new("Medium resolution", [(640, 200)], [4]),
    new("High resolution", [(640, 400)], [2]),
  ];

  /// <summary>Which ST resolution the screen holds.</summary>
  public DaliResolution Resolution { get; init; }

  /// <summary>The ST palette as big-endian 16-bit entries.</summary>
  public byte[] Palette { get; init; }

  /// <summary>Uncompressed screen bytes.</summary>
  public byte[] ScreenData { get; init; }

  /// <summary>Width, colours and bitplane count for a resolution.</summary>
  private static (int Width, int Height, int Planes) _Geometry(DaliResolution resolution) => resolution switch {
    DaliResolution.Low => (320, 200, 4),
    DaliResolution.Medium => (640, 200, 2),
    DaliResolution.High => (640, 400, 1),
    _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unknown Dali resolution.")
  };

  public static RawImage ToRawImage(DaliCompressedFile file) {
    var (width, height, planes) = _Geometry(file.Resolution);
    var colors = 1 << planes;

    var palette = new byte[colors * 3];
    for (var i = 0; i < colors; ++i) {
      var entry = (file.Palette[i * 2] << 8) | file.Palette[i * 2 + 1];
      // ST palette entries are three bits per channel, held in the low bits of each nibble.
      palette[i * 3] = (byte)(((entry >> 8) & 7) * 255 / 7);
      palette[i * 3 + 1] = (byte)(((entry >> 4) & 7) * 255 / 7);
      palette[i * 3 + 2] = (byte)((entry & 7) * 255 / 7);
    }

    var chunky = PlanarConverter.AtariStToChunky(file.ScreenData, width, height, planes);

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = chunky,
      Palette = palette,
      PaletteCount = colors,
    };
  }

  public static DaliCompressedFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // Low resolution is the only mode that can carry a colour picture, so it is the target.
    const DaliResolution resolution = DaliResolution.Low;
    var (width, height, planes) = _Geometry(resolution);
    if (image.Width != width || image.Height != height)
      throw new ArgumentException($"Expected {width}x{height} but got {image.Width}x{image.Height}.", nameof(image));

    var indexed = PixelConverter.Convert(image, PixelFormat.Indexed4);
    var rgb = indexed.Palette ?? [];

    var palette = new byte[PaletteSize];
    for (var i = 0; i < 16 && i * 3 + 2 < rgb.Length; ++i) {
      var entry = ((rgb[i * 3] * 7 / 255) << 8) | ((rgb[i * 3 + 1] * 7 / 255) << 4) | (rgb[i * 3 + 2] * 7 / 255);
      palette[i * 2] = (byte)(entry >> 8);
      palette[i * 2 + 1] = (byte)entry;
    }

    var chunky = new byte[width * height];
    for (var i = 0; i < chunky.Length; ++i) {
      var b = indexed.PixelData[i >> 1];
      chunky[i] = (byte)((i & 1) == 0 ? (b >> 4) & 0x0F : b & 0x0F);
    }

    return new() {
      Resolution = resolution,
      Palette = palette,
      ScreenData = PlanarConverter.ChunkyToAtariSt(chunky, width, height, planes),
    };
  }

  /// <summary>Formats a length the way the header stores it: ASCII decimal, then CR LF.</summary>
  internal static byte[] FormatLength(int value)
    => Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture) + "\r\n");
}
