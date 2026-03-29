using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Wmf;

/// <summary>In-memory representation of a WMF (Windows Metafile) image.</summary>
[FormatMagicBytes([0xD7, 0xCD, 0xC6, 0x9A])]
public sealed class WmfFile : IImageFileFormat<WmfFile> {

  static string IImageFileFormat<WmfFile>.PrimaryExtension => ".wmf";
  static string[] IImageFileFormat<WmfFile>.FileExtensions => [".wmf"];
  static WmfFile IImageFileFormat<WmfFile>.FromFile(FileInfo file) => WmfReader.FromFile(file);
  static WmfFile IImageFileFormat<WmfFile>.FromBytes(byte[] data) => WmfReader.FromBytes(data);
  static WmfFile IImageFileFormat<WmfFile>.FromStream(Stream stream) => WmfReader.FromStream(stream);
  static byte[] IImageFileFormat<WmfFile>.ToBytes(WmfFile file) => WmfWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw RGB24 pixel data (3 bytes per pixel, top-down).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(WmfFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static WmfFile FromRawImage(RawImage image) {
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
