using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AliasPix;

/// <summary>In-memory representation of an Alias/Wavefront PIX image.</summary>
public sealed class AliasPixFile : IImageFileFormat<AliasPixFile> {

  static string IImageFileFormat<AliasPixFile>.PrimaryExtension => ".pix";
  static string[] IImageFileFormat<AliasPixFile>.FileExtensions => [".pix", ".als", ".alias"];
  static AliasPixFile IImageFileFormat<AliasPixFile>.FromFile(FileInfo file) => AliasPixReader.FromFile(file);
  static AliasPixFile IImageFileFormat<AliasPixFile>.FromBytes(byte[] data) => AliasPixReader.FromBytes(data);
  static AliasPixFile IImageFileFormat<AliasPixFile>.FromStream(Stream stream) => AliasPixReader.FromStream(stream);
  static byte[] IImageFileFormat<AliasPixFile>.ToBytes(AliasPixFile file) => AliasPixWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int XOffset { get; init; }
  public int YOffset { get; init; }
  public int BitsPerPixel { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(AliasPixFile file) {
    ArgumentNullException.ThrowIfNull(file);

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
