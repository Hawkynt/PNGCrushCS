using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MsxView;

/// <summary>In-memory representation of an MSX View image (MSX2 Screen 8, 256x212, 256-color RGB332).</summary>
public sealed class MsxViewFile : IImageFileFormat<MsxViewFile> {

  static string IImageFileFormat<MsxViewFile>.PrimaryExtension => ".mvw";
  static string[] IImageFileFormat<MsxViewFile>.FileExtensions => [".mvw", ".msv"];
  static FormatCapability IImageFileFormat<MsxViewFile>.Capabilities => FormatCapability.IndexedOnly;
  static MsxViewFile IImageFileFormat<MsxViewFile>.FromFile(FileInfo file) => MsxViewReader.FromFile(file);
  static MsxViewFile IImageFileFormat<MsxViewFile>.FromBytes(byte[] data) => MsxViewReader.FromBytes(data);
  static MsxViewFile IImageFileFormat<MsxViewFile>.FromStream(Stream stream) => MsxViewReader.FromStream(stream);
  static byte[] IImageFileFormat<MsxViewFile>.ToBytes(MsxViewFile file) => MsxViewWriter.ToBytes(file);

  /// <summary>Fixed image width.</summary>
  public const int FixedWidth = 256;

  /// <summary>Fixed image height.</summary>
  public const int FixedHeight = 212;

  /// <summary>Expected file size in bytes.</summary>
  public const int ExpectedFileSize = FixedWidth * FixedHeight;

  /// <summary>Image width, always 256.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 212.</summary>
  public int Height => FixedHeight;

  /// <summary>Raw pixel data (54272 bytes, one byte per pixel in RGB332 encoding).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this MSX View image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(MsxViewFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var rgb = new byte[FixedWidth * FixedHeight * 3];

    for (var i = 0; i < file.PixelData.Length; ++i) {
      var val = file.PixelData[i];
      var offset = i * 3;
      rgb[offset] = (byte)((val >> 5) * 36);
      rgb[offset + 1] = (byte)(((val >> 2) & 7) * 36);
      rgb[offset + 2] = (byte)((val & 3) * 85);
    }

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Creates an MSX View image from a platform-independent <see cref="RawImage"/> by quantizing to RGB332.</summary>
  public static MsxViewFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Image must be {FixedWidth}x{FixedHeight}, got {image.Width}x{image.Height}.");
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Image must be Rgb24 format, got {image.Format}.");

    var pixels = new byte[ExpectedFileSize];
    for (var i = 0; i < ExpectedFileSize; ++i) {
      var offset = i * 3;
      var r = image.PixelData[offset];
      var g = image.PixelData[offset + 1];
      var b = image.PixelData[offset + 2];
      pixels[i] = (byte)(((r / 36) << 5) | (((g / 36) & 7) << 2) | ((b / 85) & 3));
    }

    return new() { PixelData = pixels };
  }
}
