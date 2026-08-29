using System;
using FileFormat.Core;

namespace FileFormat.Skantek;

/// <summary>In-memory representation of a Skantek page (.skn).</summary>
public readonly record struct SkantekFile : IImageFormatReader<SkantekFile>, IImageToRawImage<SkantekFile>, IImageFromRawImage<SkantekFile>, IImageFormatWriter<SkantekFile> {

  public static ReadOnlySpan<byte> Signature => [
    0xFF, 0xFF, 0x00, 0x01,
    0xFF, 0xFF, 0xFF, 0xFE,
    0xFF, 0xFD, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00,
  ];
  public const int StampOffset = 302;
  public static ReadOnlySpan<byte> Stamp => "920101"u8;
  public const int HeightOffset = 732;
  public const int WidthOffset = 736;
  public const int HeaderSize = 740;

  static string IImageFormatMetadata<SkantekFile>.PrimaryExtension => ".skn";
  static string[] IImageFormatMetadata<SkantekFile>.FileExtensions => [".skn"];
  static SkantekFile IImageFormatReader<SkantekFile>.FromSpan(ReadOnlySpan<byte> data) => SkantekReader.FromSpan(data);
  static byte[] IImageFormatWriter<SkantekFile>.ToBytes(SkantekFile file) => SkantekWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<SkantekFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])];

  static bool? IImageFormatMetadata<SkantekFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length < Signature.Length ? null : header[..Signature.Length].SequenceEqual(Signature);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

  public static RawImage ToRawImage(SkantekFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = BilevelRows.Unpack(file.PixelData ?? [], file.Width, file.Height),
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  /// <summary>Creates a bilevel Skantek page from any source image.</summary>
  public static SkantekFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width is < 1 or > 65535 || image.Height is < 1 or > 65535)
      throw new ArgumentException($"Skantek dimensions must be between 1 and 65535; got {image.Width}x{image.Height}.", nameof(image));

    var indices = BilevelRows.Threshold(image, setWhenDark: true);
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = BilevelRows.Pack(indices, image.Width, image.Height),
    };
  }
}
