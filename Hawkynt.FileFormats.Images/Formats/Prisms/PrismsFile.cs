using System;
using FileFormat.Core;

namespace FileFormat.Prisms;

/// <summary>In-memory representation of a Prisms picture (.pri, .lff).</summary>
public readonly record struct PrismsFile : IImageFormatReader<PrismsFile>, IImageToRawImage<PrismsFile>, IImageFromRawImage<PrismsFile>, IImageFormatWriter<PrismsFile> {

  public static ReadOnlySpan<byte> Signature => [0xEB, 0xE8, 0x00, 0x00];
  public const int LayoutOffset = 0x86;
  public static ReadOnlySpan<byte> Layout => "R8G8B8A8"u8;
  public const int HeightOffset = 0x1CC, WidthOffset = 0x1CE;
  public const int DataPointerOffset = 0x200;
  public const int MinFileSize = DataPointerOffset + 2;
  public const byte OpcodeLiteral = 0x10;
  public const byte OpcodeRuns = 0x20;
  public const byte OpcodeAlign = 0x00;
  public const int AlignTo = 16;

  static string IImageFormatMetadata<PrismsFile>.PrimaryExtension => ".pri";
  static string[] IImageFormatMetadata<PrismsFile>.FileExtensions => [".pri", ".lff"];
  static PrismsFile IImageFormatReader<PrismsFile>.FromSpan(ReadOnlySpan<byte> data) => PrismsReader.FromSpan(data);
  static byte[] IImageFormatWriter<PrismsFile>.ToBytes(PrismsFile file) => PrismsWriter.ToBytes(file);

  static VideoMode[] IImageFormatMetadata<PrismsFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<PrismsFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < Signature.Length)
      return null;
    if (!header[..Signature.Length].SequenceEqual(Signature))
      return false;
    if (header.Length < LayoutOffset + Layout.Length)
      return null;
    return header.Slice(LayoutOffset, Layout.Length).SequenceEqual(Layout);
  }

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PrismsFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData ?? [],
  };

  /// <summary>Creates a Prisms/LucasFilm picture using literal coding from any source image.</summary>
  public static PrismsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width is < 1 or > ushort.MaxValue || image.Height is < 1 or > ushort.MaxValue)
      throw new ArgumentException($"Prisms dimensions must fit 16-bit fields; got {image.Width}x{image.Height}.", nameof(image));
    var rgb = image.EnsureFormat(PixelFormat.Rgb24);
    return new() { Width = rgb.Width, Height = rgb.Height, PixelData = rgb.PixelData[..] };
  }
}