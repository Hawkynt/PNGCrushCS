using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.SecretPhotos;

/// <summary>In-memory representation of a SecretPhotos puzzle (.xp0).</summary>
/// <remarks>
/// A wrapper round a JPEG, which is the same shape as the ECC, LView Pro and IPSM rows already
/// closed here — except that this one states no size for the payload to agree with. What identifies
/// it instead is what XnView's own reader requires: four bytes reading <c>00 00 00 01</c>, and the
/// picture at 1779. That offset is the format rather than one sample's accident, which is what this
/// file had twice been rewritten to say could not be shown.
/// <para/>
/// A fixed offset with no length beside it needs something to stop it drawing whatever happens to
/// lie there, so the reader requires the bytes at 1779 to open a JPEG and requires that JPEG to
/// decode. A file of any other shape under this name is refused rather than read at an offset it
/// never meant.
/// </remarks>
public readonly record struct SecretPhotosFile
  : IImageFormatReader<SecretPhotosFile>, IImageToRawImage<SecretPhotosFile>, IImageFromRawImage<SecretPhotosFile>, IImageFormatWriter<SecretPhotosFile> {

  /// <summary>The four bytes every one of these opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [0x00, 0x00, 0x00, 0x01];

  /// <summary>Where the picture begins.</summary>
  public const int PictureOffset = 0x6F3;

  static string IImageFormatMetadata<SecretPhotosFile>.PrimaryExtension => ".xp0";
  static string[] IImageFormatMetadata<SecretPhotosFile>.FileExtensions => [".xp0"];
  static SecretPhotosFile IImageFormatReader<SecretPhotosFile>.FromSpan(ReadOnlySpan<byte> data) => SecretPhotosReader.FromSpan(data);
  static byte[] IImageFormatWriter<SecretPhotosFile>.ToBytes(SecretPhotosFile file) => SecretPhotosWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<SecretPhotosFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>Pixels across, as the JPEG it carries states.</summary>
  public int Width { get; init; }

  /// <summary>Pixels down, as the JPEG it carries states.</summary>
  public int Height { get; init; }

  /// <summary>The JPEG the wrapper carries, exactly as it stands in the file.</summary>
  public byte[] Embedded { get; init; }

  public static RawImage ToRawImage(SecretPhotosFile file)
    => JpegFile.ToRawImage(JpegReader.FromBytes(file.Embedded ?? throw new InvalidDataException("A SecretPhotos puzzle carries no JPEG.")));

  public static SecretPhotosFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new() {
      Width = image.Width,
      Height = image.Height,
      Embedded = JpegWriter.ToBytes(JpegFile.FromRawImage(image)),
    };
  }
}
