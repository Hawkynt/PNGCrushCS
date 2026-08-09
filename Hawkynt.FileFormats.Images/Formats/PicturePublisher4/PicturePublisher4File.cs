using System;
using FileFormat.Core;

namespace FileFormat.PicturePublisher4;

/// <summary>In-memory representation of a Micrografx Picture Publisher 4 document (.pp4).</summary>
/// <remarks>
/// Nothing published describes this format, and it turns out not to need describing: XnView reads the
/// long at 0x2A, copies everything from there to the end of the file into a temporary file, and hands
/// that file to another of its readers. Which reader was settled by construction — a TIFF, a Windows
/// bitmap, a PCX, a Targa, a PNG, a JPEG and a portable pixmap were each dropped in at the offset in
/// turn, and only the TIFF was read. So a Picture Publisher 4 document is two bytes of <c>II</c>, an
/// offset at 0x2A, and a whole TIFF standing at it.
/// <para/>
/// That makes this the same shape as the ECC, LView Pro and IPSM wrappers already here, and it is
/// identified the same way: the header's own offset has to point at something that opens as a TIFF, so
/// a foreign file that happens to begin <c>II</c> is refused rather than drawn.
/// </remarks>
public readonly record struct PicturePublisher4File
  : IImageFormatReader<PicturePublisher4File>, IImageToRawImage<PicturePublisher4File> {

  /// <summary>The two bytes the file opens with.</summary>
  public static ReadOnlySpan<byte> Signature => "II"u8;

  /// <summary>Where the offset of the embedded picture stands, as a little-endian long.</summary>
  public const int PointerOffset = 0x2A;

  /// <summary>The smallest file that can carry that offset.</summary>
  public const int MinFileSize = PointerOffset + 4;

  static string IImageFormatMetadata<PicturePublisher4File>.PrimaryExtension => ".pp4";
  static string[] IImageFormatMetadata<PicturePublisher4File>.FileExtensions => [".pp4"];
  static PicturePublisher4File IImageFormatReader<PicturePublisher4File>.FromSpan(ReadOnlySpan<byte> data)
    => PicturePublisher4Reader.FromSpan(data);

  static VideoMode[] IImageFormatMetadata<PicturePublisher4File>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>
  /// Abstains rather than claiming: <c>II</c> is also how every little-endian TIFF opens, and whether
  /// this file carries a picture is not known until the offset at 0x2A has been followed.
  /// </summary>
  static bool? IImageFormatMetadata<PicturePublisher4File>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < Signature.Length)
      return null;

    return header[..Signature.Length].SequenceEqual(Signature) ? null : false;
  }

  /// <summary>Pixels across, as the TIFF inside states.</summary>
  public int Width { get; init; }

  /// <summary>Rows, as the TIFF inside states.</summary>
  public int Height { get; init; }

  /// <summary>Where the TIFF begins.</summary>
  public int PictureOffset { get; init; }

  /// <summary>The TIFF the wrapper carries, exactly as it stands in the file.</summary>
  public byte[] Embedded { get; init; }

  public static RawImage ToRawImage(PicturePublisher4File file) => PicturePublisher4Reader.Decode(file);
}
