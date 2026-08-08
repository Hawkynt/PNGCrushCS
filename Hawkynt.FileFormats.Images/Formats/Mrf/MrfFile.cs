using System;
using FileFormat.Core;

namespace FileFormat.Mrf;

/// <summary>In-memory representation of a Monochrome Recursive Format picture (.mrf).</summary>
/// <remarks>
/// Russell Marks wrote this for <c>zgv</c>, the SVGAlib picture viewer, and it stores one thing only:
/// a bilevel picture, coded as a quadtree. Thirteen bytes of header — <c>MRF1</c>, the width and the
/// height as big-endian longs, and a byte that must be nought — and then a bit stream that runs to
/// the end of the file with nothing marking where it stops.
/// <para/>
/// The canvas is rounded up to whole tiles of sixty-four by sixty-four and the tiles are coded in
/// reading order. A square is either uniform, in which case one bit says so and a second gives the
/// colour, or it is quartered and its four halves-squares coded in turn; a square of one pixel is
/// uniform by definition and spends no bit saying so. Bits run most-significant first and do not
/// realign between tiles.
/// <para/>
/// The byte at twelve is what tells this apart from its colour sibling <c>PRF1</c>, which uses the
/// same header and reads that byte as a depth and a plane count. Nought means one bit and one plane,
/// so insisting on it is the same as insisting the file is monochrome.
/// </remarks>
public readonly record struct MrfFile : IImageFormatReader<MrfFile>, IImageToRawImage<MrfFile> {

  /// <summary>The four bytes every one of these opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [(byte)'M', (byte)'R', (byte)'F', (byte)'1'];

  /// <summary>Magic, width, height, and the byte that says one bit and one plane.</summary>
  public const int HeaderSize = 13;

  /// <summary>The side of the squares the picture is cut into before coding.</summary>
  public const int TileSize = 64;

  /// <summary>Nought is black and one is white; the file carries no palette to say otherwise.</summary>
  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFormatMetadata<MrfFile>.PrimaryExtension => ".mrf";
  static string[] IImageFormatMetadata<MrfFile>.FileExtensions => [".mrf"];
  static MrfFile IImageFormatReader<MrfFile>.FromSpan(ReadOnlySpan<byte> data) => MrfReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<MrfFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])
  ];

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>One byte per pixel, nought or one, already cropped to the stated size.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(MrfFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }
}
