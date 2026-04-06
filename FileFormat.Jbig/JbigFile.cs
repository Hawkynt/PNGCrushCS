using System;
using FileFormat.Core;

namespace FileFormat.Jbig;

/// <summary>In-memory representation of a JBIG1 (ITU-T T.82) bi-level image.</summary>
public readonly record struct JbigFile : IImageFormatReader<JbigFile>, IImageToRawImage<JbigFile>, IImageFromRawImage<JbigFile>, IImageFormatWriter<JbigFile> {

  static string IImageFormatMetadata<JbigFile>.PrimaryExtension => ".jbg";
  static string[] IImageFormatMetadata<JbigFile>.FileExtensions => [".jbg", ".bie", ".jbig"];
  static JbigFile IImageFormatReader<JbigFile>.FromSpan(ReadOnlySpan<byte> data) => JbigReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<JbigFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<JbigFile>.ToBytes(JbigFile file) => JbigWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public static RawImage ToRawImage(JbigFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed1,
    PixelData = file.PixelData[..],
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static JbigFile FromRawImage(RawImage image) {
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
