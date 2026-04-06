using System;
using FileFormat.Core;

namespace FileFormat.PsionPic;

/// <summary>In-memory representation of a Psion Series bitmap image.</summary>
public readonly record struct PsionPicFile : IImageFormatReader<PsionPicFile>, IImageToRawImage<PsionPicFile>, IImageFromRawImage<PsionPicFile>, IImageFormatWriter<PsionPicFile> {

  internal const int HeaderSize = 16;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFormatMetadata<PsionPicFile>.PrimaryExtension => ".ppic";
  static string[] IImageFormatMetadata<PsionPicFile>.FileExtensions => [".ppic"];
  static PsionPicFile IImageFormatReader<PsionPicFile>.FromSpan(ReadOnlySpan<byte> data) => PsionPicReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<PsionPicFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<PsionPicFile>.ToBytes(PsionPicFile file) => PsionPicWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PsionPicFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  public static PsionPicFile FromRawImage(RawImage image) {
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
