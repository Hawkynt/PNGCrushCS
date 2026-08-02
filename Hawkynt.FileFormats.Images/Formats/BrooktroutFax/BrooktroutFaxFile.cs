using System;
using FileFormat.Core;

namespace FileFormat.BrooktroutFax;

/// <summary>In-memory representation of a Brooktrout 301 fax image image.</summary>
public readonly record struct BrooktroutFaxFile : IImageFormatReader<BrooktroutFaxFile>, IImageToRawImage<BrooktroutFaxFile>, IImageFromRawImage<BrooktroutFaxFile>, IImageFormatWriter<BrooktroutFaxFile> {

  /// <summary>The two bytes every one of these begins with.</summary>
  internal static ReadOnlySpan<byte> Signature => [0xBB, 0x01];

  /// <summary>Offset of the width, as a 16-bit little-endian count of pixels.</summary>
  internal const int WidthOffset = 9;

  /// <summary>Offset of the height, in the same shape.</summary>
  internal const int HeightOffset = 45;

  /// <summary>Bytes before the coded page, which begins on a fixed boundary.</summary>
  internal const int HeaderSize = 128;

  /// <summary>White first: a fax states paper as zero, not ink.</summary>
  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

  static string IImageFormatMetadata<BrooktroutFaxFile>.PrimaryExtension => ".brk";
  static string[] IImageFormatMetadata<BrooktroutFaxFile>.FileExtensions => [".brk", ".301", ".brt"];
  static BrooktroutFaxFile IImageFormatReader<BrooktroutFaxFile>.FromSpan(ReadOnlySpan<byte> data) => BrooktroutFaxReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<BrooktroutFaxFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])
  ];
  static byte[] IImageFormatWriter<BrooktroutFaxFile>.ToBytes(BrooktroutFaxFile file) => BrooktroutFaxWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(BrooktroutFaxFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  public static BrooktroutFaxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed1);
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
