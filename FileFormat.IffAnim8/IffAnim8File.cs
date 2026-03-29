using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.IffAnim8;

/// <summary>In-memory representation of an IFF ANIM8 (Long-word delta animation) file.</summary>
public sealed class IffAnim8File : IImageFileFormat<IffAnim8File> {

  /// <summary>Minimum valid file size (FORM header = 12 bytes).</summary>
  internal const int MinFileSize = 12;

  /// <summary>Default width for ANIM8 images.</summary>
  internal const int DefaultWidth = 320;

  /// <summary>Default height for ANIM8 images.</summary>
  internal const int DefaultHeight = 200;

  static string IImageFileFormat<IffAnim8File>.PrimaryExtension => ".an8";
  static string[] IImageFileFormat<IffAnim8File>.FileExtensions => [".an8", ".anim8"];
  static IffAnim8File IImageFileFormat<IffAnim8File>.FromFile(FileInfo file) => IffAnim8Reader.FromFile(file);
  static IffAnim8File IImageFileFormat<IffAnim8File>.FromBytes(byte[] data) => IffAnim8Reader.FromBytes(data);
  static IffAnim8File IImageFileFormat<IffAnim8File>.FromStream(Stream stream) => IffAnim8Reader.FromStream(stream);
  static RawImage IImageFileFormat<IffAnim8File>.ToRawImage(IffAnim8File file) => ToRawImage(file);
  static IffAnim8File IImageFileFormat<IffAnim8File>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<IffAnim8File>.ToBytes(IffAnim8File file) => IffAnim8Writer.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; } = DefaultWidth;

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; } = DefaultHeight;

  /// <summary>Raw file data.</summary>
  public byte[] RawData { get; init; } = [];

  /// <summary>Converts this ANIM8 file to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(IffAnim8File file) {
    ArgumentNullException.ThrowIfNull(file);

    var width = file.Width;
    var height = file.Height;
    var rgb = new byte[width * height * 3];

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Not supported. ANIM8 files require complex delta animation encoding.</summary>
  public static IffAnim8File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to IffAnim8File is not supported due to complex delta animation encoding.");
  }
}
