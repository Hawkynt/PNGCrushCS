using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.AutoFx;

/// <summary>In-memory representation of an Auto F/X picture (.afx).</summary>
/// <remarks>
/// Opens with PNG's eight-byte signature carrying <c>AFX</c> where PNG writes its own name, and is
/// not chunked the way a PNG is — the resemblance stops at the signature. One sample carries the
/// notice "© 1996 Auto F/X Corporation - All Rights Reserved".
/// <para/>
/// Most of these hold four JPEGs, and picking one by searching for the first is exactly the mistake
/// this format punishes: the first two are 140x88 and 128x80 in every single file and are
/// byte-for-byte identical across all twelve samples — they are the program's own furniture, not
/// anybody's picture. The last, where present, is a thumbnail of at most 128 pixels on its long side
/// whose aspect ratio matches the real picture's to within a rounding.
/// <para/>
/// The header says which one plainly. A big-endian long at 284 is the offset the picture begins at
/// and the one at 288 is how long it runs, and in all twelve samples those two add up to the length
/// of the file exactly — including the four that carry only a single JPEG, where the offset is 1024.
/// Nothing is searched for: the reader goes where the header points and refuses the file unless a
/// JPEG starts there and the arithmetic closes.
/// </remarks>
public readonly record struct AutoFxFile
  : IImageFormatReader<AutoFxFile>, IImageToRawImage<AutoFxFile>,
    IImageFromRawImage<AutoFxFile>, IImageFormatWriter<AutoFxFile> {

  /// <summary>The eight bytes every one of these opens with: PNG's signature, renamed.</summary>
  public static ReadOnlySpan<byte> Magic => [0x89, (byte)'A', (byte)'F', (byte)'X', 0x0D, 0x0A, 0x1A, 0x0A];

  /// <summary>Where the header points at the picture, and states how long it runs — big-endian longs.</summary>
  internal const int PictureOffsetAt = 284, PictureLengthAt = 288;

  /// <summary>Where the pictures begin in the four samples that carry nothing else, and what this writes.</summary>
  public const int DefaultPictureOffset = 1024;

  static string IImageFormatMetadata<AutoFxFile>.PrimaryExtension => ".afx";
  static string[] IImageFormatMetadata<AutoFxFile>.FileExtensions => [".afx"];
  static AutoFxFile IImageFormatReader<AutoFxFile>.FromSpan(ReadOnlySpan<byte> data) => AutoFxReader.FromSpan(data);
  static byte[] IImageFormatWriter<AutoFxFile>.ToBytes(AutoFxFile file) => AutoFxWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AutoFxFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>Where in the file the picture began, as the header stated.</summary>
  public int PictureOffset { get; init; }

  /// <summary>The JPEG the header points at, from its offset to the end of the file.</summary>
  public byte[] Embedded { get; init; }

  public static RawImage ToRawImage(AutoFxFile file)
    => JpegFile.ToRawImage(JpegReader.FromBytes(file.Embedded ?? throw new InvalidDataException("An Auto F/X picture carries no JPEG.")));

  public static AutoFxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() {
      PictureOffset = DefaultPictureOffset,
      Embedded = JpegWriter.ToBytes(JpegFile.FromRawImage(image)),
    };
  }
}
