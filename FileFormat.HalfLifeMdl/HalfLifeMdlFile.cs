using System;
using FileFormat.Core;

namespace FileFormat.HalfLifeMdl;

/// <summary>In-memory representation of a Half-Life Model texture image.</summary>
public readonly record struct HalfLifeMdlFile : IImageFormatReader<HalfLifeMdlFile>, IImageToRawImage<HalfLifeMdlFile>, IImageFromRawImage<HalfLifeMdlFile>, IImageFormatWriter<HalfLifeMdlFile> {

  internal const int HeaderSize = 16;

  static string IImageFormatMetadata<HalfLifeMdlFile>.PrimaryExtension => ".mdltex";
  static string[] IImageFormatMetadata<HalfLifeMdlFile>.FileExtensions => [".mdltex"];
  static HalfLifeMdlFile IImageFormatReader<HalfLifeMdlFile>.FromSpan(ReadOnlySpan<byte> data) => HalfLifeMdlReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<HalfLifeMdlFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<HalfLifeMdlFile>.ToBytes(HalfLifeMdlFile file) => HalfLifeMdlWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(HalfLifeMdlFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
    };
  }

  public static HalfLifeMdlFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed8.", nameof(image));
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
