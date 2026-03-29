using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.GraphSaurus;

/// <summary>In-memory representation of a Graph Saurus image (MSX2 Screen 8, 256x212, 256-color RGB332).</summary>
public sealed class GraphSaurusFile : IImageFileFormat<GraphSaurusFile> {

  static string IImageFileFormat<GraphSaurusFile>.PrimaryExtension => ".grs";
  static string[] IImageFileFormat<GraphSaurusFile>.FileExtensions => [".grs", ".sr5", ".sr7", ".sr8", ".srs"];
  static FormatCapability IImageFileFormat<GraphSaurusFile>.Capabilities => FormatCapability.IndexedOnly;
  static GraphSaurusFile IImageFileFormat<GraphSaurusFile>.FromFile(FileInfo file) => GraphSaurusReader.FromFile(file);
  static GraphSaurusFile IImageFileFormat<GraphSaurusFile>.FromBytes(byte[] data) => GraphSaurusReader.FromBytes(data);
  static GraphSaurusFile IImageFileFormat<GraphSaurusFile>.FromStream(Stream stream) => GraphSaurusReader.FromStream(stream);
  static byte[] IImageFileFormat<GraphSaurusFile>.ToBytes(GraphSaurusFile file) => GraphSaurusWriter.ToBytes(file);

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

  /// <summary>Converts this Graph Saurus image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(GraphSaurusFile file) {
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

  /// <summary>Creates a Graph Saurus image from a platform-independent <see cref="RawImage"/> by quantizing to RGB332.</summary>
  public static GraphSaurusFile FromRawImage(RawImage image) {
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
