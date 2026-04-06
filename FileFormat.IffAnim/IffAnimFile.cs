using System;
using FileFormat.Core;

namespace FileFormat.IffAnim;

/// <summary>In-memory representation of an IFF ANIM animation container (first frame only).</summary>
[FormatMagicBytes([0x46, 0x4F, 0x52, 0x4D])]
public readonly record struct IffAnimFile : IImageFormatReader<IffAnimFile>, IImageToRawImage<IffAnimFile>, IImageFromRawImage<IffAnimFile>, IImageFormatWriter<IffAnimFile> {

  static string IImageFormatMetadata<IffAnimFile>.PrimaryExtension => ".anim";
  static string[] IImageFormatMetadata<IffAnimFile>.FileExtensions => [".anim"];
  static IffAnimFile IImageFormatReader<IffAnimFile>.FromSpan(ReadOnlySpan<byte> data) => IffAnimReader.FromSpan(data);

  static bool? IImageFormatMetadata<IffAnimFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12 && header[0] == 0x46 && header[1] == 0x4F && header[2] == 0x52 && header[3] == 0x4D
      && header[8] == 0x41 && header[9] == 0x4E && header[10] == 0x49 && header[11] == 0x4D;

  static byte[] IImageFormatWriter<IffAnimFile>.ToBytes(IffAnimFile file) => IffAnimWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>RGB24 pixel data from the first ILBM frame.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(IffAnimFile file) {
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
