using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Uhdr;

/// <summary>In-memory representation of a UHDR (Ultra HDR) image.</summary>
[FormatMagicBytes([0x55, 0x48, 0x44, 0x52])]
public sealed class UhdrFile : IImageFileFormat<UhdrFile> {

  static string IImageFileFormat<UhdrFile>.PrimaryExtension => ".uhdr";
  static string[] IImageFileFormat<UhdrFile>.FileExtensions => [".uhdr"];
  static UhdrFile IImageFileFormat<UhdrFile>.FromFile(FileInfo file) => UhdrReader.FromFile(file);
  static UhdrFile IImageFileFormat<UhdrFile>.FromBytes(byte[] data) => UhdrReader.FromBytes(data);
  static UhdrFile IImageFileFormat<UhdrFile>.FromStream(Stream stream) => UhdrReader.FromStream(stream);
  static byte[] IImageFileFormat<UhdrFile>.ToBytes(UhdrFile file) => UhdrWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw RGB pixel data (3 bytes per pixel).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(UhdrFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static UhdrFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
