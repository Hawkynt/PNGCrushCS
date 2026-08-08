using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.SyberiaTexture;

/// <summary>In-memory representation of a Syberia texture (.syj).</summary>
/// <remarks>
/// A JPEG with the first ten bytes cut off: the start-of-image marker, the APP0 marker, its length,
/// and four of the five bytes of "JFIF\0". A file begins at that string's terminating nought, which is
/// why one opens 00 01 02 01 00 48 00 48 — the version, the density units and the two densities of a
/// JFIF block whose head is missing.
/// <para/>
/// Putting the ten bytes back makes every sample an ordinary JPEG, and all three then match XnView.
/// </remarks>
public readonly record struct SyberiaTextureFile
  : IImageFormatReader<SyberiaTextureFile>, IImageToRawImage<SyberiaTextureFile>,
    IImageFromRawImage<SyberiaTextureFile>, IImageFormatWriter<SyberiaTextureFile> {

  static string IImageFormatMetadata<SyberiaTextureFile>.PrimaryExtension => ".syj";
  static string[] IImageFormatMetadata<SyberiaTextureFile>.FileExtensions => [".syj"];
  static SyberiaTextureFile IImageFormatReader<SyberiaTextureFile>.FromSpan(ReadOnlySpan<byte> data) => SyberiaTextureReader.FromSpan(data);
  static byte[] IImageFormatWriter<SyberiaTextureFile>.ToBytes(SyberiaTextureFile file) => SyberiaTextureWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<SyberiaTextureFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>The ten bytes a file is missing: start of image, then the head of a JFIF block.</summary>
  internal static ReadOnlySpan<byte> MissingHead => [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F'];

  /// <summary>The JPEG this file is, with its head put back.</summary>
  public byte[] Restored { get; init; }

  public static RawImage ToRawImage(SyberiaTextureFile file)
    => JpegFile.ToRawImage(JpegReader.FromBytes(file.Restored ?? throw new InvalidDataException("A Syberia texture carries no picture.")));

  public static SyberiaTextureFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() { Restored = JpegWriter.ToBytes(JpegFile.FromRawImage(image)) };
  }
}
