using System;
using FileFormat.Core;

namespace FileFormat.Skantek;

/// <summary>In-memory representation of a Skantek page (.skn).</summary>
/// <remarks>
/// Nothing describes this format anywhere; the layout was read out of XnView's own converter and then
/// put back to it, which is what says it is right. A page built to the header below is reported at the
/// size it states and comes back pixel for pixel as the page that was coded.
/// <para/>
/// The file opens with four big-endian longs that never vary — <c>FFFF0001</c>, <c>FFFFFFFE</c>,
/// <c>FFFD0000</c> and zero — and the converter refuses the file unless all four are as written. 286
/// bytes are then skipped, and the six characters <c>920101</c> stand at offset 302; that too is
/// required, and it reads as a date the format was fixed on rather than as a name. 424 further bytes
/// are skipped, which puts the size at 732: the height first, as a big-endian long, then the width at
/// 736. The coded page begins at 740.
/// <para/>
/// The coding is Group 4 with the bits running from the bottom of each byte upwards rather than the
/// top down. In the converter that is a word in its fax context choosing an identity byte table over a
/// bit-reversal one; here the bytes are turned over before decoding, which comes to the same thing.
/// Decoding the stream the ordinary way round produces a blank page, which is how the choice was
/// found.
/// </remarks>
public readonly record struct SkantekFile : IImageFormatReader<SkantekFile>, IImageToRawImage<SkantekFile> {

  /// <summary>The sixteen bytes the file opens with, all four longs of them fixed.</summary>
  public static ReadOnlySpan<byte> Signature => [
    0xFF, 0xFF, 0x00, 0x01,
    0xFF, 0xFF, 0xFF, 0xFE,
    0xFF, 0xFD, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00,
  ];

  /// <summary>Where the six characters <c>920101</c> stand.</summary>
  public const int StampOffset = 302;

  /// <summary>The six characters that stand there.</summary>
  public static ReadOnlySpan<byte> Stamp => "920101"u8;

  /// <summary>Where the height stands, as a big-endian long.</summary>
  public const int HeightOffset = 732;

  /// <summary>Where the width stands, as a big-endian long.</summary>
  public const int WidthOffset = 736;

  /// <summary>How long the header is, which is where the coding begins.</summary>
  public const int HeaderSize = 740;

  static string IImageFormatMetadata<SkantekFile>.PrimaryExtension => ".skn";
  static string[] IImageFormatMetadata<SkantekFile>.FileExtensions => [".skn"];
  static SkantekFile IImageFormatReader<SkantekFile>.FromSpan(ReadOnlySpan<byte> data) => SkantekReader.FromSpan(data);

  static VideoMode[] IImageFormatMetadata<SkantekFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])
  ];

  static bool? IImageFormatMetadata<SkantekFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length < Signature.Length ? null : header[..Signature.Length].SequenceEqual(Signature);

  /// <summary>Pixels across, as the header states.</summary>
  public int Width { get; init; }

  /// <summary>Rows, as the header states.</summary>
  public int Height { get; init; }

  /// <summary>Packed rows, a set bit being ink.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

  public static RawImage ToRawImage(SkantekFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = BilevelRows.Unpack(file.PixelData ?? [], file.Width, file.Height),
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };
}
