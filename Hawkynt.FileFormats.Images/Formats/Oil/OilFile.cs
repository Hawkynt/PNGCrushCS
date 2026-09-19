using System;
using FileFormat.Core;

namespace FileFormat.Oil;

/// <summary>In-memory representation of an OIL (Open Image Library) picture.</summary>
public readonly record struct OilFile : IImageFormatReader<OilFile>, IImageToRawImage<OilFile>, IImageFromRawImage<OilFile>, IImageFormatWriter<OilFile> {

  public static ReadOnlySpan<byte> Signature => "OIL\0"u8;
  public const uint MagicNumber = 0x693D71;
  public const ushort SupportedVersion = 1;
  public const string HeadString = "This is a graphics file based on the Open Image Library file format specification.";
  public const int HeadStringLength = 83;
  public const int HeaderSize = 4 + 4 + 2 + 4 + 4 + 4 + HeadStringLength;
  public const int DirectoryEntrySize = 255 + 4 + 4;
  public const int ImageHeaderSize = 4 + 4 + 4 + 5 + 4 + 4;
  public const byte TypePalette = 1;
  public const byte TypeLuminance = 2;
  public const byte TypeBgr = 3;
  public const byte TypeBgra = 4;
  public const byte CompressionNone = 0;
  public const byte CompressionRle = 1;
  public const byte CompressionLzo = 2;
  public const byte CompressionZlib = 3;
  public const int PaletteEntrySize = 4;

  static string IImageFormatMetadata<OilFile>.PrimaryExtension => ".oil";
  static string[] IImageFormatMetadata<OilFile>.FileExtensions => [".oil"];
  static OilFile IImageFormatReader<OilFile>.FromSpan(ReadOnlySpan<byte> data) => OilReader.FromSpan(data);
  static byte[] IImageFormatWriter<OilFile>.ToBytes(OilFile file) => OilWriter.ToBytes(file);

  static VideoMode[] IImageFormatMetadata<OilFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<OilFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 8)
      return null;

    return header[..4].SequenceEqual(Signature)
      && header[4] == (MagicNumber & 0xFF)
      && header[5] == ((MagicNumber >> 8) & 0xFF)
      && header[6] == ((MagicNumber >> 16) & 0xFF)
      && header[7] == 0;
  }

  public int Width { get; init; }
  public int Height { get; init; }
  public PixelFormat Format { get; init; }
  public byte[] PixelData { get; init; }
  public byte[]? Palette { get; init; }
  public int PaletteCount { get; init; }

  public static RawImage ToRawImage(OilFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = file.Format,
    PixelData = file.PixelData[..],
    Palette = file.Palette?[..],
    PaletteCount = file.PaletteCount,
  };

  /// <summary>Creates an uncompressed OIL v1 image from any source while preserving alpha.</summary>
  public static OilFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width <= 0 || image.Height <= 0)
      throw new ArgumentException("OIL dimensions must be positive.", nameof(image));
    var rgba = image.EnsureFormat(PixelFormat.Rgba32);
    return new() {
      Width = rgba.Width,
      Height = rgba.Height,
      Format = PixelFormat.Rgba32,
      PixelData = rgba.PixelData[..],
    };
  }
}