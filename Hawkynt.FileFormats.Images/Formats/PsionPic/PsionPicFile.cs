using System;
using FileFormat.Core;

namespace FileFormat.PsionPic;

/// <summary>In-memory representation of a Psion PIC bitmap.</summary>
/// <remarks>
/// Six bytes of "PIC" 0xDC "00", a count of bitmaps, and then a twelve-byte record for each: a
/// checksum, the width, the height, the bytes the bitmap takes, and where it sits relative to the end
/// of its own record. Rows are padded to a whole number of sixteen-bit words, one bit a pixel, and the
/// bits run from the least significant end of each byte.
/// <para/>
/// A file may hold more than one. The three samples hold one, one and two, and where there are two the
/// second is the mask for the first — it comes out as the negative of it — so the first is the picture
/// and the one the tools draw.
/// </remarks>
public readonly record struct PsionPicFile
  : IImageFormatReader<PsionPicFile>, IImageToRawImage<PsionPicFile> {

  static string IImageFormatMetadata<PsionPicFile>.PrimaryExtension => ".pic";
  static string[] IImageFormatMetadata<PsionPicFile>.FileExtensions => [".pic", ".icn", ".ch3"];
  static PsionPicFile IImageFormatReader<PsionPicFile>.FromSpan(ReadOnlySpan<byte> data) => PsionPicReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<PsionPicFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])
  ];

  static bool? IImageFormatMetadata<PsionPicFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= Magic.Length && header[..Magic.Length].SequenceEqual(Magic) ? true : null;

  /// <summary>The six bytes a file opens with.</summary>
  internal static ReadOnlySpan<byte> Magic => [(byte)'P', (byte)'I', (byte)'C', 0xDC, (byte)'0', (byte)'0'];

  /// <summary>Bytes each bitmap's record takes.</summary>
  internal const int RecordSize = 12;

  /// <summary>Where the first record sits.</summary>
  internal const int FirstRecord = 8;

  /// <summary>Paper then ink: a clear bit is white and a set bit black.</summary>
  private static readonly byte[] _BlackAndWhite = [255, 255, 255, 0, 0, 0];

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bitmaps the file holds, of which the first is the picture.</summary>
  public int Count { get; init; }

  /// <summary>One index per pixel.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PsionPicFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = file.PixelData[..],
    Palette = _BlackAndWhite[..],
    PaletteCount = 2,
  };
}
