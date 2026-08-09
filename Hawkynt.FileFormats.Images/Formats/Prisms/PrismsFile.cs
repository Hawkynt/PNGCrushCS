using System;
using FileFormat.Core;

namespace FileFormat.Prisms;

/// <summary>In-memory representation of a Prisms picture (.pri, .lff).</summary>
/// <remarks>
/// XnView carries this format twice: once as "Prisms" with <c>.pri</c> and once as "LucasFilm Format"
/// with <c>.lff</c>, and both names point at the same reader in its own table — so they are one format
/// under two names. dexvert, which has never seen that table, identifies its Lucasfilm Picture by the
/// five characters <c>Prims</c>, which is the very name this reader writes into its own info block.
/// Two independent sources therefore say a <c>.lff</c> opens the way a <c>.pri</c> does.
/// <para/>
/// The layout came out of the converter and was confirmed by construction — a file built to it comes
/// back at the size it states with every pixel as it went in. Four bytes <c>EB E8 00 00</c>, the eight
/// characters <c>R8G8B8A8</c> at 0x86, the height and then the width as sixteen-bit little-endian
/// numbers at 0x1CC, and a sixteen-bit offset at 0x200 saying where the coded picture begins.
/// <para/>
/// The coding is a stream of two-byte commands, a count and an opcode. Opcode 0x10 is a literal run of
/// count-plus-one pixels read straight from the file. Opcode 0x20 is count-plus-one run-length groups,
/// each a byte of length followed by one pixel repeated length-plus-one times. Opcode zero with a count
/// of zero steps the reader on to the next sixteen-byte boundary, and every other opcode is a
/// two-byte no-op. A row ends when it is full, and the rows run from the bottom of the picture upwards.
/// <para/>
/// A pixel is four bytes. Despite what the header calls itself, the red, green and blue the converter
/// draws are the fourth, third and second of them; the first is not drawn.
/// </remarks>
public readonly record struct PrismsFile : IImageFormatReader<PrismsFile>, IImageToRawImage<PrismsFile> {

  /// <summary>The four bytes the file opens with.</summary>
  public static ReadOnlySpan<byte> Signature => [0xEB, 0xE8, 0x00, 0x00];

  /// <summary>Where the eight characters naming the pixel layout stand.</summary>
  public const int LayoutOffset = 0x86;

  /// <summary>Those eight characters.</summary>
  public static ReadOnlySpan<byte> Layout => "R8G8B8A8"u8;

  /// <summary>Where the height stands, with the width right behind it.</summary>
  public const int HeightOffset = 0x1CC, WidthOffset = 0x1CE;

  /// <summary>Where the offset of the coded picture stands.</summary>
  public const int DataPointerOffset = 0x200;

  /// <summary>The smallest a file can be and still hold everything the header needs.</summary>
  public const int MinFileSize = DataPointerOffset + 2;

  /// <summary>A literal run of count-plus-one pixels.</summary>
  public const byte OpcodeLiteral = 0x10;

  /// <summary>Count-plus-one run-length groups.</summary>
  public const byte OpcodeRuns = 0x20;

  /// <summary>With a count of zero, steps on to the next sixteen-byte boundary.</summary>
  public const byte OpcodeAlign = 0x00;

  /// <summary>How wide the boundary that opcode aligns to is.</summary>
  public const int AlignTo = 16;

  static string IImageFormatMetadata<PrismsFile>.PrimaryExtension => ".pri";
  static string[] IImageFormatMetadata<PrismsFile>.FileExtensions => [".pri", ".lff"];
  static PrismsFile IImageFormatReader<PrismsFile>.FromSpan(ReadOnlySpan<byte> data) => PrismsReader.FromSpan(data);

  static VideoMode[] IImageFormatMetadata<PrismsFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<PrismsFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < Signature.Length)
      return null;

    if (!header[..Signature.Length].SequenceEqual(Signature))
      return false;

    // Four bytes are a thin signature on their own, so the layout string is required too when enough
    // of the header is in hand to look at it.
    if (header.Length < LayoutOffset + Layout.Length)
      return null;

    return header.Slice(LayoutOffset, Layout.Length).SequenceEqual(Layout);
  }

  /// <summary>Pixels across, as the header states.</summary>
  public int Width { get; init; }

  /// <summary>Rows, as the header states.</summary>
  public int Height { get; init; }

  /// <summary>Three bytes a pixel, top row first.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PrismsFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData ?? [],
  };
}
