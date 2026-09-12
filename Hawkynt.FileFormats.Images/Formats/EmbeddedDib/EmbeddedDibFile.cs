using System;
using FileFormat.Core;

namespace FileFormat.EmbeddedDib;

/// <summary>A packed Windows device-independent bitmap: bitmap information followed directly by its pixels.</summary>
/// <remarks>
/// A standalone BMP puts a 14-byte <c>BITMAPFILEHEADER</c> in front of the DIB. The packed DIB form
/// deliberately omits that file header; Microsoft uses the same representation when a bitmap is
/// embedded in another format, and ImageMagick exposes it as the standalone <c>DIB</c> format.
/// <para/>
/// The unrelated drawing/project formats that used to be grouped under this type merely happen to
/// contain a DIB preview somewhere inside their own container. They remain available through
/// <see cref="EmbeddedDibPreviewFile"/> and are not writable as DIB files under somebody else's name.
/// </remarks>
[FormatMagicBytes([0x28, 0x00, 0x00, 0x00])]
[FormatMagicBytes([0x34, 0x00, 0x00, 0x00])]
[FormatMagicBytes([0x38, 0x00, 0x00, 0x00])]
[FormatMagicBytes([0x6C, 0x00, 0x00, 0x00])]
[FormatMagicBytes([0x7C, 0x00, 0x00, 0x00])]
public readonly record struct EmbeddedDibFile
  : IImageFormatReader<EmbeddedDibFile>, IImageToRawImage<EmbeddedDibFile>,
    IImageFromRawImage<EmbeddedDibFile>, IImageFormatWriter<EmbeddedDibFile> {

  /// <summary>The shortest and longest Windows information header this accepts.</summary>
  public const int MinHeaderSize = 40, MaxHeaderSize = 124;

  /// <summary>No picture carried by the callers of this helper comes near this; it also bounds malformed input.</summary>
  public const int MaxDimension = 20000;

  static string IImageFormatMetadata<EmbeddedDibFile>.PrimaryExtension => ".dib";
  static string[] IImageFormatMetadata<EmbeddedDibFile>.FileExtensions => [".dib"];
  static EmbeddedDibFile IImageFormatReader<EmbeddedDibFile>.FromSpan(ReadOnlySpan<byte> data)
    => EmbeddedDibReader.FromSpan(data);
  static byte[] IImageFormatWriter<EmbeddedDibFile>.ToBytes(EmbeddedDibFile file)
    => EmbeddedDibWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<EmbeddedDibFile>.VideoModes => [
    new("Default", [(new IntegerRange(1, MaxDimension), new IntegerRange(1, MaxDimension))])
  ];

  /// <summary>The decoded bitmap.</summary>
  public RawImage Preview { get; init; }

  /// <summary>Where the DIB began in the input. Zero for an ordinary standalone <c>.dib</c>.</summary>
  public int Offset { get; init; }

  public static RawImage ToRawImage(EmbeddedDibFile file)
    => file.Preview ?? throw new InvalidOperationException("No bitmap was read.");

  public static EmbeddedDibFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new() { Preview = image, Offset = 0 };
  }
}
