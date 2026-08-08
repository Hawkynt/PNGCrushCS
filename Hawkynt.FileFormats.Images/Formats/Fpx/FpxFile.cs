using System;
using FileFormat.Core;

namespace FileFormat.Fpx;

/// <summary>In-memory representation of a FlashPix picture.</summary>
/// <remarks>
/// This used to declare a magic of <c>FPX\0</c> and read a width, a height and raw RGB behind it.
/// No FlashPix file has ever looked like that: a FlashPix picture is a Microsoft compound file, and
/// the four bytes were an invention shared with the writer that stood beside this. Both are gone.
/// <para/>
/// The writer is gone rather than corrected because writing one would mean assembling a compound
/// file, a property set and a JPEG table set around a picture that has to be tiled and coded first —
/// and nothing but this would read the result. Reading is the job.
/// </remarks>
[FormatMagicBytes([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1])]
public readonly record struct FpxFile : IImageFormatReader<FpxFile>, IImageToRawImage<FpxFile> {

  static string IImageFormatMetadata<FpxFile>.PrimaryExtension => ".fpx";

  /// <summary><c>.mix</c> is Microsoft Picture It! and PhotoDraw, which store a FlashPix inside.</summary>
  /// <remarks>
  /// Nineteen of the twenty-one <c>.mix</c> samples here are compound files carrying the same Data
  /// Object Store, Resolution and Subimage structure a <c>.fpx</c> does, so they are the same
  /// picture format under another program's name. The other two are neither, and are refused on the
  /// signature.
  /// </remarks>
  static string[] IImageFormatMetadata<FpxFile>.FileExtensions => [".fpx", ".mix"];

  static bool? IImageFormatMetadata<FpxFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length < 8 ? null : CompoundFile.HasSignature(header) ? null : false;

  static FpxFile IImageFormatReader<FpxFile>.FromSpan(ReadOnlySpan<byte> data) => FpxReader.FromSpan(data);

  static VideoMode[] IImageFormatMetadata<FpxFile>.VideoModes
    => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])];

  /// <summary>Width of the largest resolution the pyramid holds.</summary>
  public int Width { get; init; }

  /// <summary>Height of the largest resolution the pyramid holds.</summary>
  public int Height { get; init; }

  /// <summary>The assembled tiles, three bytes a pixel.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(FpxFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData ?? [],
  };
}
