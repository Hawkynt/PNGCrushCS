using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Dwg;

/// <summary>An AutoCAD drawing (.dwg), read by the thumbnail it states the address of.</summary>
/// <remarks>
/// The drawing itself is a compressed, sectioned, bit-packed database of entities whose layout
/// changes with every release; reading it is a project rather than a reader, and half of what it
/// holds is not a picture at all. What every version of it does carry, and states the address of in
/// its first twenty bytes, is the thumbnail a file chooser shows.
/// <para/>
/// Byte 13 of the file header holds that address. At it sits a sixteen-byte sentinel, then the
/// length of the block, then a count of the pictures in it, then that many descriptors of nine
/// bytes each — a type, an offset from the start of the file, and a length. Type 1 is a title
/// block, type 2 a Windows bitmap with its file header left off, type 3 a metafile with the Aldus
/// header on, and type 6 a PNG entire. The block ends with the sentinel again, every byte
/// complemented, which is what says the whole thing has been read as it was meant.
/// <para/>
/// Both samples here are AC1027 and carry a PNG at the stated offset, the sentinel and its
/// complement both agreeing. Nothing about the drawing's own contents is read or guessed at.
/// <para/>
/// It does not write, because a thumbnail with no drawing behind it is not a drawing.
/// </remarks>
public readonly record struct DwgFile : IImageFormatReader<DwgFile>, IImageToRawImage<DwgFile> {

  /// <summary>The sixteen bytes the thumbnail block opens with.</summary>
  public static ReadOnlySpan<byte> ImageSentinel => [
    0x1F, 0x25, 0x6D, 0x07, 0xD4, 0x36, 0x28, 0x28,
    0x9D, 0x57, 0xCA, 0x3F, 0x9D, 0x44, 0x10, 0x2B
  ];

  /// <summary>Where the file header states the thumbnail block's address.</summary>
  public const int ImageSeekerOffset = 0x0D;

  /// <summary>The version string, five zeros, a maintenance byte, a flag, and then the seeker.</summary>
  public const int MinimumHeaderSize = ImageSeekerOffset + 4;

  /// <summary>A type, an offset and a length.</summary>
  public const int ImageDescriptorSize = 9;

  /// <summary>The four picture types a thumbnail block can hold.</summary>
  public const int TypeHeaderData = 1, TypeBitmap = 2, TypeMetafile = 3, TypePng = 6;

  static string IImageFormatMetadata<DwgFile>.PrimaryExtension => ".dwg";
  static string[] IImageFormatMetadata<DwgFile>.FileExtensions => [".dwg"];
  static DwgFile IImageFormatReader<DwgFile>.FromSpan(ReadOnlySpan<byte> data) => DwgReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<DwgFile>.VideoModes => [
    new("Thumbnail", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<DwgFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 6)
      return null;

    // "AC" and four digits: AC1009 is R11, AC1032 is 2018, and everything in between is one of these.
    if (header[0] != 'A' || header[1] != 'C')
      return false;

    for (var i = 2; i < 6; ++i)
      if (header[i] is < (byte)'0' or > (byte)'9')
        return false;

    return true;
  }

  /// <summary>The thumbnail, already decoded.</summary>
  public RawImage Thumbnail { get; init; }

  /// <summary>Which of the picture types the thumbnail was stored as.</summary>
  public int ThumbnailType { get; init; }

  /// <summary>The six-character version string the file opens with, such as <c>AC1027</c>.</summary>
  public string Version { get; init; }

  public static RawImage ToRawImage(DwgFile file)
    => file.Thumbnail ?? throw new InvalidDataException("An AutoCAD drawing carries no thumbnail this could read.");
}
