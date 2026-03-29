using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SharpX68k;

/// <summary>Sharp X68000 16-bit color screen data model.</summary>
public sealed class SharpX68kFile : IImageFileFormat<SharpX68kFile> {

  public const int HeaderSize = 8;

  public int Width { get; init; } = 512;
  public int Height { get; init; } = 512;
  public byte[] PixelData { get; init; } = [];


  public static string PrimaryExtension => ".x68";
  public static string[] FileExtensions => [".x68", ".x68k"];
  public static SharpX68kFile FromFile(FileInfo file) => SharpX68kReader.FromFile(file);
  public static SharpX68kFile FromBytes(byte[] data) => SharpX68kReader.FromBytes(data);
  public static SharpX68kFile FromStream(Stream stream) => SharpX68kReader.FromStream(stream);
  public static byte[] ToBytes(SharpX68kFile file) => SharpX68kWriter.ToBytes(file);

  public static RawImage ToRawImage(SharpX68kFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var pixels = file.PixelData[..];
    return new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,

    };
  }

  public static SharpX68kFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected Rgb24, got {image.Format}");
    var pixels = image.PixelData[..];
    return new SharpX68kFile {
      Width = image.Width,
      Height = image.Height,
      PixelData = pixels,
    };
  }
}
