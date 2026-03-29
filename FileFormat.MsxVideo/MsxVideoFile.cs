using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MsxVideo;

/// <summary>In-memory representation of a Video MSX screen capture (MSX2 Screen 8, 256x212, 256-color RGB332).</summary>
public sealed class MsxVideoFile : IImageFileFormat<MsxVideoFile> {

  static string IImageFileFormat<MsxVideoFile>.PrimaryExtension => ".mvi";
  static string[] IImageFileFormat<MsxVideoFile>.FileExtensions => [".mvi"];
  static FormatCapability IImageFileFormat<MsxVideoFile>.Capabilities => FormatCapability.IndexedOnly;
  static MsxVideoFile IImageFileFormat<MsxVideoFile>.FromFile(FileInfo file) => MsxVideoReader.FromFile(file);
  static MsxVideoFile IImageFileFormat<MsxVideoFile>.FromBytes(byte[] data) => MsxVideoReader.FromBytes(data);
  static MsxVideoFile IImageFileFormat<MsxVideoFile>.FromStream(Stream stream) => MsxVideoReader.FromStream(stream);
  static byte[] IImageFileFormat<MsxVideoFile>.ToBytes(MsxVideoFile file) => MsxVideoWriter.ToBytes(file);

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

  /// <summary>Converts this Video MSX image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(MsxVideoFile file) {
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

  /// <summary>Creates a Video MSX image from a platform-independent <see cref="RawImage"/> by quantizing to RGB332.</summary>
  public static MsxVideoFile FromRawImage(RawImage image) {
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
