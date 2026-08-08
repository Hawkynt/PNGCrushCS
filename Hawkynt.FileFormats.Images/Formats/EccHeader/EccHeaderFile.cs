using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.EccHeader;

/// <summary>In-memory representation of an ECC picture (.ecc).</summary>
/// <remarks>
/// A short header opening with <c>ECCH</c>, and then an ordinary PNG. The header states the size
/// twice — at 12 and again at 16, both little-endian words — and the sample says 640 by 400 in both
/// pairs, which is exactly what the PNG it carries says of itself.
/// <para/>
/// That agreement is what identifies the file rather than a fixed offset for the picture: only one
/// sample exists, so where its PNG happens to begin is not known to be where every one begins. The
/// reader looks for the signature and then refuses the file unless the picture it found is the size
/// the header promised, so a coincidental match cannot be drawn as if it were the picture.
/// </remarks>
public readonly record struct EccHeaderFile
  : IImageFormatReader<EccHeaderFile>, IImageToRawImage<EccHeaderFile>,
    IImageFromRawImage<EccHeaderFile>, IImageFormatWriter<EccHeaderFile> {

  /// <summary>The four bytes every one of these opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [(byte)'E', (byte)'C', (byte)'C', (byte)'H'];

  /// <summary>Where the header states the size, as two little-endian words, and states it again.</summary>
  internal const int WidthAt = 12, HeightAt = 14, SecondWidthAt = 16, SecondHeightAt = 18;

  /// <summary>The header this writes, which is the length the sample's picture begins at.</summary>
  public const int DefaultPictureOffset = 88;

  static string IImageFormatMetadata<EccHeaderFile>.PrimaryExtension => ".ecc";
  static string[] IImageFormatMetadata<EccHeaderFile>.FileExtensions => [".ecc"];
  static EccHeaderFile IImageFormatReader<EccHeaderFile>.FromSpan(ReadOnlySpan<byte> data) => EccHeaderReader.FromSpan(data);
  static byte[] IImageFormatWriter<EccHeaderFile>.ToBytes(EccHeaderFile file) => EccHeaderWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<EccHeaderFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>Pixels across, as the header states.</summary>
  public int Width { get; init; }

  /// <summary>Pixels down, as the header states.</summary>
  public int Height { get; init; }

  /// <summary>The PNG the wrapper carries, exactly as it stands in the file.</summary>
  public byte[] Embedded { get; init; }

  public static RawImage ToRawImage(EccHeaderFile file)
    => PngFile.ToRawImage(PngReader.FromBytes(file.Embedded ?? throw new InvalidDataException("An ECC picture carries no PNG.")));

  public static EccHeaderFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Embedded = PngWriter.ToBytes(PngFile.FromRawImage(image)),
    };
  }
}
