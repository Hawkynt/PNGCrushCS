using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.IffAnim;

/// <summary>In-memory representation of an IFF ANIM animation container (first frame only).</summary>
[FormatMagicBytes([0x46, 0x4F, 0x52, 0x4D])]
public sealed class IffAnimFile : IImageFileFormat<IffAnimFile> {

  static string IImageFileFormat<IffAnimFile>.PrimaryExtension => ".anim";
  static string[] IImageFileFormat<IffAnimFile>.FileExtensions => [".anim"];

  static bool? IImageFileFormat<IffAnimFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12 && header[0] == 0x46 && header[1] == 0x4F && header[2] == 0x52 && header[3] == 0x4D
      && header[8] == 0x41 && header[9] == 0x4E && header[10] == 0x49 && header[11] == 0x4D;

  static IffAnimFile IImageFileFormat<IffAnimFile>.FromFile(FileInfo file) => IffAnimReader.FromFile(file);
  static IffAnimFile IImageFileFormat<IffAnimFile>.FromBytes(byte[] data) => IffAnimReader.FromBytes(data);
  static IffAnimFile IImageFileFormat<IffAnimFile>.FromStream(Stream stream) => IffAnimReader.FromStream(stream);
  static byte[] IImageFileFormat<IffAnimFile>.ToBytes(IffAnimFile file) => IffAnimWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>RGB24 pixel data from the first ILBM frame.</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(IffAnimFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static IffAnimFile FromRawImage(RawImage image) {
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
