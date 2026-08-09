using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Tiff;

namespace FileFormat.Eroiica;

/// <summary>An Eroiica document (.eif), and the pictures its pages are made of.</summary>
/// <remarks>
/// Eroiica was a document viewer for engineering drawings, and its file is a compound document
/// rather than a picture: a set description, a page list, text, and the scanned rasters. It opens
/// with eight bytes that are not letters — <c>7C 3E 24 24 27 58 21 01</c> — and every one of them is
/// required. That is not a guess: XnView's own converter was handed the sample here with one byte of
/// the head inverted at a time, and inverting any of the first eight made it stop recognising the
/// file while inverting any of the next sixteen did not.
/// <para/>
/// The rasters are stored as whole TIFF files, each complete in itself with its own byte-order mark,
/// its own image file directory and offsets counted from its own first byte. So a page is found by
/// looking for one and then requiring it to account for itself: the directory has to parse, every
/// entry's value has to stand inside the file, and the strips the directory points at have to end
/// inside it too. A run of pixels that happens to begin with <c>II*</c> does not survive that.
/// <para/>
/// Reading the first such stream is what XnView does, which the same byte-flipping showed: breaking
/// the first TIFF's byte-order mark made it report the second one's size instead of failing, and
/// breaking that TIFF's directory pointer made it fail outright. What is read here is all of them,
/// in the order they stand, with the first as the picture — the sample carries five, a 259x197
/// colour illustration and then four 2068x1581 Group 4 scans of the drawing.
/// <para/>
/// The illustration decoded from the extracted stream by ImageMagick and XnView's decode of the
/// whole document are the same 259x197 picture on every byte, which is what says the streams are
/// standalone TIFFs rather than something that merely starts like one.
/// <para/>
/// Nothing is written: a document is a page list, a set description and the text on the pages, and
/// none of that is modelled here.
/// </remarks>
[FormatMagicBytes([0x7C, 0x3E, 0x24, 0x24, 0x27, 0x58, 0x21, 0x01])]
public sealed class EroiicaFile
  : IImageFormatReader<EroiicaFile>, IImageToRawImage<EroiicaFile>,
    IMultiImageFileFormat<EroiicaFile> {

  /// <summary>The eight bytes a document opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [0x7C, 0x3E, 0x24, 0x24, 0x27, 0x58, 0x21, 0x01];

  static string IImageFormatMetadata<EroiicaFile>.PrimaryExtension => ".eif";
  static string[] IImageFormatMetadata<EroiicaFile>.FileExtensions => [".eif"];
  static EroiicaFile IImageFormatReader<EroiicaFile>.FromSpan(ReadOnlySpan<byte> data) => EroiicaReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<EroiicaFile>.Capabilities => FormatCapability.MultiImage;
  static VideoMode[] IImageFormatMetadata<EroiicaFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2, 256, 16777216])
  ];

  static bool? IImageFormatMetadata<EroiicaFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= Magic.Length && header[..Magic.Length].SequenceEqual(Magic) ? true : null;

  /// <summary>Each embedded TIFF stream, whole, in the order the document stores them.</summary>
  public IReadOnlyList<byte[]> Pages { get; init; } = [];

  public static int ImageCount(EroiicaFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Pages.Count;
  }

  public static RawImage ToRawImage(EroiicaFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Pages.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    return TiffFile.ToRawImage(TiffReader.FromSpan(file.Pages[index]));
  }

  public static RawImage ToRawImage(EroiicaFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Pages.Count == 0)
      throw new InvalidDataException("An Eroiica document with no raster page in it.");

    return ToRawImage(file, 0);
  }
}
