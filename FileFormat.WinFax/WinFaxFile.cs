using System;
using FileFormat.Core;

namespace FileFormat.WinFax;

/// <summary>In-memory representation of a WinFAX fax image image.</summary>
public readonly record struct WinFaxFile : IImageFormatReader<WinFaxFile>, IImageToRawImage<WinFaxFile>, IImageFromRawImage<WinFaxFile>, IImageFormatWriter<WinFaxFile> {

  internal const int HeaderSize = 16;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFormatMetadata<WinFaxFile>.PrimaryExtension => ".fxs";
  static string[] IImageFormatMetadata<WinFaxFile>.FileExtensions => [".fxs", ".fxo", ".fxr", ".fxd", ".fxm"];
  static WinFaxFile IImageFormatReader<WinFaxFile>.FromSpan(ReadOnlySpan<byte> data) => WinFaxReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<WinFaxFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<WinFaxFile>.ToBytes(WinFaxFile file) => WinFaxWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(WinFaxFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  public static WinFaxFile FromRawImage(RawImage image) {
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
