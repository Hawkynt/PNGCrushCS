using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SeattleFilmWorks;

/// <summary>In-memory representation of a Seattle Film Works (SFW/PWP) image.</summary>
/// <remarks>
/// SFW files are JFIF/JPEG images with a proprietary header prepended.
/// The format uses a "SFW94A" 6-byte magic signature followed by embedded JPEG data.
/// PWP files use "SFW95A" magic and contain multiple concatenated SFW images.
/// </remarks>
[FormatMagicBytes([0x53, 0x46, 0x57, 0x39, 0x34, 0x41])] // "SFW94A"
[FormatMagicBytes([0x53, 0x46, 0x57, 0x39, 0x35, 0x41])] // "SFW95A"
public sealed class SeattleFilmWorksFile : IImageFileFormat<SeattleFilmWorksFile> {

  /// <summary>SFW magic header: "SFW94A" (6 bytes).</summary>
  internal static readonly byte[] SfwMagic = [0x53, 0x46, 0x57, 0x39, 0x34, 0x41];

  /// <summary>PWP magic header: "SFW95A" (6 bytes).</summary>
  internal static readonly byte[] PwpMagic = [0x53, 0x46, 0x57, 0x39, 0x35, 0x41];

  /// <summary>JPEG SOI marker: 0xFF 0xD8.</summary>
  internal static readonly byte[] JpegSoi = [0xFF, 0xD8];

  /// <summary>The magic header length in bytes.</summary>
  internal const int MAGIC_LENGTH = 6;

  /// <summary>Minimum valid file size: 6-byte magic + 2-byte JPEG SOI.</summary>
  internal const int MIN_FILE_SIZE = MAGIC_LENGTH + 2;

  static string IImageFileFormat<SeattleFilmWorksFile>.PrimaryExtension => ".sfw";
  static string[] IImageFileFormat<SeattleFilmWorksFile>.FileExtensions => [".sfw", ".pwp"];
  static SeattleFilmWorksFile IImageFileFormat<SeattleFilmWorksFile>.FromFile(FileInfo file) => SeattleFilmWorksReader.FromFile(file);
  static SeattleFilmWorksFile IImageFileFormat<SeattleFilmWorksFile>.FromBytes(byte[] data) => SeattleFilmWorksReader.FromBytes(data);
  static SeattleFilmWorksFile IImageFileFormat<SeattleFilmWorksFile>.FromStream(Stream stream) => SeattleFilmWorksReader.FromStream(stream);
  static byte[] IImageFileFormat<SeattleFilmWorksFile>.ToBytes(SeattleFilmWorksFile file) => SeattleFilmWorksWriter.ToBytes(file);

  static bool? IImageFileFormat<SeattleFilmWorksFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < MAGIC_LENGTH)
      return null;

    if (header[..MAGIC_LENGTH].SequenceEqual(SfwMagic))
      return true;

    if (header[..MAGIC_LENGTH].SequenceEqual(PwpMagic))
      return true;

    return false;
  }

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>The raw embedded JPEG data (starting from FFD8).</summary>
  public byte[] JpegData { get; init; } = [];

  /// <summary>Raw RGB pixel data (3 bytes per pixel, Width * Height * 3 bytes total).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(SeattleFilmWorksFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static SeattleFilmWorksFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    var pixelData = image.PixelData[..];

    return new() {
      Width = image.Width,
      Height = image.Height,
      JpegData = [],
      PixelData = pixelData,
    };
  }
}
