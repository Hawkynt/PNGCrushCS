using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.RiscOsSprite;

/// <summary>Acorn RISC OS sprite format data model.</summary>
public sealed class RiscOsSpriteFile : IImageFileFormat<RiscOsSpriteFile> {

  public const int HeaderSize = 16;

  public int Width { get; init; } = 320;
  public int Height { get; init; } = 256;
  public byte[] PixelData { get; init; } = [];


  public static string PrimaryExtension => ".spr";
  public static string[] FileExtensions => [".spr", ".ros"];
  public static RiscOsSpriteFile FromFile(FileInfo file) => RiscOsSpriteReader.FromFile(file);
  public static RiscOsSpriteFile FromBytes(byte[] data) => RiscOsSpriteReader.FromBytes(data);
  public static RiscOsSpriteFile FromStream(Stream stream) => RiscOsSpriteReader.FromStream(stream);
  public static byte[] ToBytes(RiscOsSpriteFile file) => RiscOsSpriteWriter.ToBytes(file);

  public static RawImage ToRawImage(RiscOsSpriteFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var pixels = file.PixelData[..];
    return new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,

    };
  }

  public static RiscOsSpriteFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected Rgb24, got {image.Format}");
    var pixels = image.PixelData[..];
    return new RiscOsSpriteFile {
      Width = image.Width,
      Height = image.Height,
      PixelData = pixels,
    };
  }
}
