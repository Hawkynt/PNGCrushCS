using System;
using FileFormat.Core;

namespace FileFormat.AliasPix;

/// <summary>In-memory representation of an Alias/Wavefront PIX image.</summary>
public readonly record struct AliasPixFile : IImageFormatReader<AliasPixFile>, IImageToRawImage<AliasPixFile>, IImageFromRawImage<AliasPixFile>, IImageFormatWriter<AliasPixFile> {

  static string IImageFormatMetadata<AliasPixFile>.PrimaryExtension => ".pix";
  static string[] IImageFormatMetadata<AliasPixFile>.FileExtensions => [".pix", ".als", ".alias"];
  static AliasPixFile IImageFormatReader<AliasPixFile>.FromSpan(ReadOnlySpan<byte> data) => AliasPixReader.FromSpan(data);
  static byte[] IImageFormatWriter<AliasPixFile>.ToBytes(AliasPixFile file) => AliasPixWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int XOffset { get; init; }
  public int YOffset { get; init; }
  public int BitsPerPixel { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(AliasPixFile file) {

    var format = file.BitsPerPixel switch {
      24 => PixelFormat.Bgr24,
      32 => PixelFormat.Bgra32,
      _ => throw new ArgumentException($"Unsupported BitsPerPixel: {file.BitsPerPixel}", nameof(file))
    };

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = file.PixelData[..],
    };
  }

  public static AliasPixFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var bpp = image.Format switch {
      PixelFormat.Bgr24 => 24,
      PixelFormat.Bgra32 => 32,
      _ => throw new ArgumentException($"Unsupported pixel format for AliasPix: {image.Format}", nameof(image))
    };

    return new() {
      Width = image.Width,
      Height = image.Height,
      BitsPerPixel = bpp,
      PixelData = image.PixelData[..],
    };
  }
}
