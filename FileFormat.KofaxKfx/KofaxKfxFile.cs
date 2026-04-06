using System;
using FileFormat.Core;

namespace FileFormat.KofaxKfx;

/// <summary>In-memory representation of a Kofax Group 4 fax image image.</summary>
public readonly record struct KofaxKfxFile : IImageFormatReader<KofaxKfxFile>, IImageToRawImage<KofaxKfxFile>, IImageFromRawImage<KofaxKfxFile>, IImageFormatWriter<KofaxKfxFile> {

  internal const int HeaderSize = 16;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFormatMetadata<KofaxKfxFile>.PrimaryExtension => ".kfx";
  static string[] IImageFormatMetadata<KofaxKfxFile>.FileExtensions => [".kfx"];
  static KofaxKfxFile IImageFormatReader<KofaxKfxFile>.FromSpan(ReadOnlySpan<byte> data) => KofaxKfxReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<KofaxKfxFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<KofaxKfxFile>.ToBytes(KofaxKfxFile file) => KofaxKfxWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(KofaxKfxFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  public static KofaxKfxFile FromRawImage(RawImage image) {
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
