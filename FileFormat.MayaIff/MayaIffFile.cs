using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MayaIff;

/// <summary>In-memory representation of a Maya IFF (FOR4/CIMG) image.</summary>
public sealed class MayaIffFile : IImageFileFormat<MayaIffFile> {

  static string IImageFileFormat<MayaIffFile>.PrimaryExtension => ".iff";
  static string[] IImageFileFormat<MayaIffFile>.FileExtensions => [".iff", ".maya"];

  static bool? IImageFileFormat<MayaIffFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12 && header[0] == 0x46 && header[1] == 0x4F && header[2] == 0x52 && header[3] == 0x34
      && header[8] == 0x43 && header[9] == 0x49 && header[10] == 0x4D && header[11] == 0x47;

  static MayaIffFile IImageFileFormat<MayaIffFile>.FromFile(FileInfo file) => MayaIffReader.FromFile(file);
  static MayaIffFile IImageFileFormat<MayaIffFile>.FromBytes(byte[] data) => MayaIffReader.FromBytes(data);
  static MayaIffFile IImageFileFormat<MayaIffFile>.FromStream(Stream stream) => MayaIffReader.FromStream(stream);
  static byte[] IImageFileFormat<MayaIffFile>.ToBytes(MayaIffFile file) => MayaIffWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Whether the image contains an alpha channel (RGBA vs RGB).</summary>
  public bool HasAlpha { get; init; }

  /// <summary>Raw pixel data: RGBA (4 bpp) when <see cref="HasAlpha"/> is true, RGB (3 bpp) otherwise.</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(MayaIffFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = file.HasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static MayaIffFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format is not (PixelFormat.Rgba32 or PixelFormat.Rgb24))
      throw new ArgumentException($"Expected {PixelFormat.Rgba32} or {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      HasAlpha = image.Format == PixelFormat.Rgba32,
      PixelData = image.PixelData[..],
    };
  }
}
