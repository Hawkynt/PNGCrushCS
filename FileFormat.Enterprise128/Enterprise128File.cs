using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Enterprise128;

/// <summary>Enterprise 128/Elan screen dump data model.</summary>
public sealed class Enterprise128File : IImageFormatReader<Enterprise128File>, IImageToRawImage<Enterprise128File>, IImageFromRawImage<Enterprise128File>, IImageFormatWriter<Enterprise128File> {

  public const int FileSize = 16384;
  public const int ImageWidth = 512;
  public const int ImageHeight = 256;

  public int Width { get; init; } = ImageWidth;
  public int Height { get; init; } = ImageHeight;
  public byte[] PixelData { get; init; } = [];
  public byte[] Palette { get; init; } = [0, 0, 0, 255, 255, 255];

  public static string PrimaryExtension => ".ep";
  public static string[] FileExtensions => [".ep", ".elan"];
  static Enterprise128File IImageFormatReader<Enterprise128File>.FromSpan(ReadOnlySpan<byte> data) => Enterprise128Reader.FromSpan(data);
  public static FormatCapability Capabilities => FormatCapability.MonochromeOnly;
  public static Enterprise128File FromFile(FileInfo file) => Enterprise128Reader.FromFile(file);
  public static Enterprise128File FromBytes(byte[] data) => Enterprise128Reader.FromBytes(data);
  public static Enterprise128File FromStream(Stream stream) => Enterprise128Reader.FromStream(stream);
  public static byte[] ToBytes(Enterprise128File file) => Enterprise128Writer.ToBytes(file);

  public static RawImage ToRawImage(Enterprise128File file) {
    ArgumentNullException.ThrowIfNull(file);
    var pixels = file.PixelData[..];
    return new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed1,
      PixelData = pixels,
      Palette = file.Palette[..],
      PaletteCount = 2,
    };
  }

  public static Enterprise128File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected Indexed1, got {image.Format}");
    if (image.Width != ImageWidth || image.Height != ImageHeight)
      throw new ArgumentException($"Expected {ImageWidth}x{ImageHeight}, got {image.Width}x{image.Height}");
    var pixels = image.PixelData[..];
    return new Enterprise128File {
      PixelData = pixels,
      Palette = image.Palette != null ? image.Palette[..] : new byte[6],
    };
  }
}
