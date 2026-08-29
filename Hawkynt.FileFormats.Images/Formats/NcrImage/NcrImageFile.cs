using System;
using FileFormat.Core;

namespace FileFormat.NcrImage;

/// <summary>In-memory representation of an NCR Image (.ncr).</summary>
public readonly record struct NcrImageFile : IImageFormatReader<NcrImageFile>, IImageToRawImage<NcrImageFile>, IImageFromRawImage<NcrImageFile>, IImageFormatWriter<NcrImageFile> {

  public static ReadOnlySpan<byte> Signature => [0x6E, 0x6E, 0x0A, 0x00];
  public const int WidthOffset = 0x42;
  public const int HeightOffset = 0x46;
  public const int CodingOffset = 0x4A;
  public const int CodedDataOffset = 0x5E;

  static string IImageFormatMetadata<NcrImageFile>.PrimaryExtension => ".ncr";
  static string[] IImageFormatMetadata<NcrImageFile>.FileExtensions => [".ncr"];
  static NcrImageFile IImageFormatReader<NcrImageFile>.FromSpan(ReadOnlySpan<byte> data) => NcrImageReader.FromSpan(data);
  static byte[] IImageFormatWriter<NcrImageFile>.ToBytes(NcrImageFile file) => NcrImageWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<NcrImageFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])];

  static bool? IImageFormatMetadata<NcrImageFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length < Signature.Length ? null : header[..Signature.Length].SequenceEqual(Signature);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

  public static RawImage ToRawImage(NcrImageFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = BilevelRows.Unpack(file.PixelData ?? [], file.Width, file.Height),
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  /// <summary>Creates a conforming Group-4 NCR Image from any source image.</summary>
  public static NcrImageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width is < 1 or > ushort.MaxValue || image.Height is < 1 or > ushort.MaxValue)
      throw new ArgumentException($"NCR dimensions must fit 16-bit fields; got {image.Width}x{image.Height}.", nameof(image));

    var indices = BilevelRows.Threshold(image, setWhenDark: true);
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = BilevelRows.Pack(indices, image.Width, image.Height),
    };
  }
}
