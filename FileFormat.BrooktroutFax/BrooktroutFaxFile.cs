using System;
using FileFormat.Core;

namespace FileFormat.BrooktroutFax;

/// <summary>In-memory representation of a Brooktrout 301 fax image image.</summary>
public readonly record struct BrooktroutFaxFile : IImageFormatReader<BrooktroutFaxFile>, IImageToRawImage<BrooktroutFaxFile>, IImageFromRawImage<BrooktroutFaxFile>, IImageFormatWriter<BrooktroutFaxFile> {

  internal const int HeaderSize = 32;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFormatMetadata<BrooktroutFaxFile>.PrimaryExtension => ".brk";
  static string[] IImageFormatMetadata<BrooktroutFaxFile>.FileExtensions => [".brk", ".301", ".brt"];
  static BrooktroutFaxFile IImageFormatReader<BrooktroutFaxFile>.FromSpan(ReadOnlySpan<byte> data) => BrooktroutFaxReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<BrooktroutFaxFile>.Capabilities => FormatCapability.MonochromeOnly;
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
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed1.", nameof(image));
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
