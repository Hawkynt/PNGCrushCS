using System;
using FileFormat.Core;

namespace FileFormat.IffAnim8;

/// <summary>In-memory representation of an IFF ANIM method-8 (short/long vertical delta) file.</summary>
public readonly record struct IffAnim8File : IImageFormatReader<IffAnim8File>, IImageToRawImage<IffAnim8File>, IImageFromRawImage<IffAnim8File>, IImageFormatWriter<IffAnim8File> {

  /// <summary>Minimum valid file size (FORM header = 12 bytes).</summary>
  internal const int MinFileSize = 12;

  /// <summary>Default width for ANIM8 images.</summary>
  internal const int DefaultWidth = 320;

  /// <summary>Default height for ANIM8 images.</summary>
  internal const int DefaultHeight = 200;

  static string IImageFormatMetadata<IffAnim8File>.PrimaryExtension => ".an8";
  static string[] IImageFormatMetadata<IffAnim8File>.FileExtensions => [".an8", ".anim8"];
  static IffAnim8File IImageFormatReader<IffAnim8File>.FromSpan(ReadOnlySpan<byte> data) => IffAnim8Reader.FromSpan(data);
  static byte[] IImageFormatWriter<IffAnim8File>.ToBytes(IffAnim8File file) => IffAnim8Writer.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>RGB24 pixels used when creating a new ANIM8 file.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Raw file data retained when a file was parsed by the legacy reader.</summary>
  public byte[] RawData { get; init; }

  /// <summary>Converts this ANIM8 file to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(IffAnim8File file) {
    var width = file.Width;
    var height = file.Height;
    var expectedLength = (long)width * height * 3;

    if (file.PixelData is { } pixels && pixels.LongLength == expectedLength)
      return new() {
        Width = width,
        Height = height,
        Format = PixelFormat.Rgb24,
        PixelData = pixels[..],
      };

    var rgb = new byte[checked(width * height * 3)];
    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Creates an ANIM8 file representation from arbitrary image pixels.</summary>
  public static IffAnim8File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      RawData = [],
    };
  }

}
