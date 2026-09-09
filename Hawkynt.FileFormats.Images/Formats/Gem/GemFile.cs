using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Gem;

/// <summary>A GEM metafile (.gem): a recording of the VDI calls that drew a picture.</summary>
/// <remarks>
/// Digital Research's Virtual Device Interface had one array of parameters for every call, and a
/// metafile is that array written down. Twenty-four words of header — the magic <c>0xFFFF</c>, the
/// header's own length so a longer one can be skipped, a version, whether coordinates are normalised
/// or raster, the extent of what was drawn, the page in tenths of a millimetre, and the coordinate
/// window that page stands for — and then one record per call: an opcode, how many points follow,
/// how many integers follow, a sub-opcode, and then the points and the integers. A word of
/// <c>0xFFFF</c> ends it, which is what <c>v_clswk</c> writes.
/// <para/>
/// All forty-two samples here walk from the header to that terminating word and land on it exactly,
/// with no bytes left over, which is what says the records are being read as the format lays them
/// out rather than as they happen to fall.
/// <para/>
/// The picture is rendered at the size the file states: the extent, measured in coordinate-window
/// units, is the fraction of the page it occupies, and the page is stated in tenths of a millimetre,
/// so the extent has a physical size and that size is taken at ninety-six pixels to the inch.
/// <para/>
/// What is drawn is the geometry: polylines, filled areas, bars, circles, ellipses, arcs, pies and
/// rounded boxes, with the line and fill attributes the file sets, and the fill patterns are the
/// VDI's own sixteen-by-sixteen tables rather than a shade invented to stand in for them. What is
/// not drawn is text — the twenty-two justified strings across all forty-two files would need the
/// GEM fonts to place, and those are not in the file.
/// <para/>
/// Writing a raster uses only standard VDI calls: the image is reduced to the workstation's sixteen
/// standard colours and horizontal runs are emitted as solid <c>v_bar</c> primitives. A GEM
/// metafile has no portable inline RGB raster payload — its cell-array call depends on control-array
/// fields that the metafile record does not store, while GEM Paint's bitmap escape points at a
/// separate IMG file — so tracing the raster into one-unit-high bars keeps the result self-contained.
/// </remarks>
public readonly record struct GemFile
  : IImageFormatReader<GemFile>, IImageToRawImage<GemFile>,
    IImageFromRawImage<GemFile>, IImageFormatWriter<GemFile> {

  /// <summary>The word every metafile opens with, and the word that ends the record list.</summary>
  public const short Magic = -1;

  /// <summary>How many words the header the format defines takes; a longer one is skipped by its own count.</summary>
  public const int StandardHeaderWords = 24;

  /// <summary>A record's opcode, point count, integer count and sub-opcode.</summary>
  public const int RecordHeaderWords = 4;

  /// <summary>The coordinate flag saying points are raster coordinates rather than normalised ones.</summary>
  public const int RasterCoordinates = 2;

  /// <summary>The normalised coordinate range assumed when the file states no window of its own.</summary>
  public const int NormalisedExtent = 32767;

  static string IImageFormatMetadata<GemFile>.PrimaryExtension => ".gem";
  static string[] IImageFormatMetadata<GemFile>.FileExtensions => [".gem"];
  static GemFile IImageFormatReader<GemFile>.FromSpan(ReadOnlySpan<byte> data) => GemReader.FromSpan(data);
  static byte[] IImageFormatWriter<GemFile>.ToBytes(GemFile file) => GemWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<GemFile>.VideoModes => [
    new("Drawing", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<GemFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 6)
      return null;

    if (header[0] != 0xFF || header[1] != 0xFF)
      return false;

    // The second word is the header's own length, which is at least the standard one.
    var words = header[2] | (header[3] << 8);
    return words >= StandardHeaderWords && words <= 512;
  }

  /// <summary>The version the file states, as major times a hundred plus minor.</summary>
  public int Version { get; init; }

  /// <summary>Whether the file states raster coordinates rather than normalised ones.</summary>
  public int CoordinateFlag { get; init; }

  /// <summary>The extent of what was drawn, in the file's own coordinates.</summary>
  public (int X1, int Y1, int X2, int Y2) Extent { get; init; }

  /// <summary>The page the drawing sits on, in tenths of a millimetre.</summary>
  public (int Width, int Height) PageSize { get; init; }

  /// <summary>The coordinate window the page stands for, in the file's own coordinates.</summary>
  public (int X1, int Y1, int X2, int Y2) Window { get; init; }

  /// <summary>Whether the header says this metafile refers to an external bit image.</summary>
  public bool HasBitImage { get; init; }

  /// <summary>Every record in the file, in order.</summary>
  public IReadOnlyList<GemRecord> Records { get; init; }

  public static RawImage ToRawImage(GemFile file) => GemRenderer.Render(file);

  /// <summary>Creates a self-contained metafile drawing a palette-reduced raster as horizontal bars.</summary>
  public static GemFile FromRawImage(RawImage image) => GemWriter.FromRawImage(image);
}
