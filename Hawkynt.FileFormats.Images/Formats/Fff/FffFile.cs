using System;
using System.Text;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Fff;

/// <summary>A MAGGI Hairstyles &amp; Cosmetics file (.fff): a client record with a JPEG portrait in it.</summary>
[FormatMagicBytes([
  (byte)'h', (byte)'a', (byte)'i', (byte)'r', (byte)'s', (byte)'t', (byte)'y', (byte)'l', (byte)'e', (byte)'s',
  (byte)' ', (byte)'&', (byte)' ',
  (byte)'c', (byte)'o', (byte)'s', (byte)'m', (byte)'e', (byte)'t', (byte)'i', (byte)'c', (byte)' ', (byte)' ', 0x00
], SignatureOffset)]
public sealed class FffFile : IImageFormatReader<FffFile>, IImageToRawImage<FffFile>, IImageFromRawImage<FffFile>, IImageFormatWriter<FffFile> {

  public const int SignatureOffset = 0x1C4;
  public const int SignatureSize = 24;
  public const int PictureOffset = 0xCC8;
  public static ReadOnlySpan<byte> Magic => "hairstyles & cosmetic  \0"u8;
  public static string SignatureText => Encoding.ASCII.GetString(Magic[..^1]);

  static string IImageFormatMetadata<FffFile>.PrimaryExtension => ".fff";
  static string[] IImageFormatMetadata<FffFile>.FileExtensions => [".fff"];
  static FffFile IImageFormatReader<FffFile>.FromSpan(ReadOnlySpan<byte> data) => FffReader.FromSpan(data);
  static byte[] IImageFormatWriter<FffFile>.ToBytes(FffFile file) => FffWriter.ToBytes(file);

  public byte[] PictureData { get; init; } = [];

  public static RawImage ToRawImage(FffFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.PictureData.Length == 0)
      throw new InvalidOperationException("No picture was read.");

    return JpegFile.ToRawImage(JpegReader.FromBytes(file.PictureData));
  }

  /// <summary>Creates a MAGGI record with the source image encoded as its JPEG portrait.</summary>
  public static FffFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new() { PictureData = JpegWriter.ToBytes(JpegFile.FromRawImage(image)) };
  }
}
