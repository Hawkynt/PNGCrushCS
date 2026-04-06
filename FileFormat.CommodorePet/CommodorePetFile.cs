using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.CommodorePet;

/// <summary>Commodore PET PETSCII screen dump data model.</summary>
public sealed class CommodorePetFile : IImageFormatReader<CommodorePetFile>, IImageToRawImage<CommodorePetFile>, IImageFromRawImage<CommodorePetFile>, IImageFormatWriter<CommodorePetFile> {

  public const int FileSize = 1000;
  public const int ImageWidth = 40;
  public const int ImageHeight = 25;

  public int Width { get; init; } = ImageWidth;
  public int Height { get; init; } = ImageHeight;
  public byte[] PixelData { get; init; } = [];
  public byte[] Palette { get; init; } = new byte[768];

  public static string PrimaryExtension => ".pet";
  public static string[] FileExtensions => [".pet"];
  static CommodorePetFile IImageFormatReader<CommodorePetFile>.FromSpan(ReadOnlySpan<byte> data) => CommodorePetReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<CommodorePetFile>.Capabilities => FormatCapability.IndexedOnly;
  public static CommodorePetFile FromFile(FileInfo file) => CommodorePetReader.FromFile(file);
  public static CommodorePetFile FromBytes(byte[] data) => CommodorePetReader.FromBytes(data);
  public static CommodorePetFile FromStream(Stream stream) => CommodorePetReader.FromStream(stream);
  public static byte[] ToBytes(CommodorePetFile file) => CommodorePetWriter.ToBytes(file);

  public static RawImage ToRawImage(CommodorePetFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var pixels = file.PixelData[..];
    return new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = file.Palette[..],
      PaletteCount = 256,
    };
  }

  public static CommodorePetFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected Indexed8, got {image.Format}");
    if (image.Width != ImageWidth || image.Height != ImageHeight)
      throw new ArgumentException($"Expected {ImageWidth}x{ImageHeight}, got {image.Width}x{image.Height}");
    var pixels = image.PixelData[..];
    return new CommodorePetFile {
      PixelData = pixels,
      Palette = image.Palette != null ? image.Palette[..] : new byte[768],
    };
  }
}
