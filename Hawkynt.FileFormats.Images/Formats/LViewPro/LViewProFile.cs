using System;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.LViewPro;

/// <summary>In-memory representation of an LView Pro image file (.lvp).</summary>
/// <remarks>
/// Two bytes of magic, the words "LView Pro Image File" and a terminator, a depth, the size as two
/// little-endian longs, and then an ordinary JPEG. The sample says 384 by 480 and the JPEG it
/// carries is 384 by 480, which is what says the size is read from the right place.
/// <para/>
/// Only one sample exists, so where its JPEG happens to begin is not known to be where every one
/// begins. The reader finds the signature and then refuses the file unless the picture it found is
/// the size the header promised, so a coincidental match cannot be drawn as the picture.
/// </remarks>
public readonly record struct LViewProFile
  : IImageFormatReader<LViewProFile>, IImageToRawImage<LViewProFile>,
    IImageFromRawImage<LViewProFile>, IImageFormatWriter<LViewProFile> {

  /// <summary>The two bytes every one of these opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [0xFE, 0xCA];

  /// <summary>The words that follow the magic, terminated by a null.</summary>
  public const string Title = "LView Pro Image File";

  internal const int TitleAt = 4;

  /// <summary>Where the depth, then the size as two little-endian longs, stand.</summary>
  internal const int DepthAt = 25, WidthAt = 29, HeightAt = 33;

  /// <summary>The header this writes, which is the length the sample's picture begins at.</summary>
  public const int DefaultPictureOffset = 101;

  static string IImageFormatMetadata<LViewProFile>.PrimaryExtension => ".lvp";
  static string[] IImageFormatMetadata<LViewProFile>.FileExtensions => [".lvp"];
  static LViewProFile IImageFormatReader<LViewProFile>.FromSpan(ReadOnlySpan<byte> data) => LViewProReader.FromSpan(data);
  static byte[] IImageFormatWriter<LViewProFile>.ToBytes(LViewProFile file) => LViewProWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<LViewProFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>Pixels across, as the header states.</summary>
  public int Width { get; init; }

  /// <summary>Pixels down, as the header states.</summary>
  public int Height { get; init; }

  /// <summary>Bits a pixel, as the header states.</summary>
  public int Depth { get; init; }

  /// <summary>The JPEG the wrapper carries, exactly as it stands in the file.</summary>
  public byte[] Embedded { get; init; }

  public static RawImage ToRawImage(LViewProFile file)
    => JpegFile.ToRawImage(JpegReader.FromBytes(file.Embedded ?? throw new InvalidDataException("An LView Pro file carries no JPEG.")));

  public static LViewProFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Depth = 8,
      Embedded = JpegWriter.ToBytes(JpegFile.FromRawImage(image)),
    };
  }
}
