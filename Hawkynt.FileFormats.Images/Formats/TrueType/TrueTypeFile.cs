using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.TrueType;

/// <summary>A TrueType font (.ttf), drawn as a sheet of its glyphs.</summary>
/// <remarks>
/// Built from Microsoft's OpenType specification — <em>Font File Structure</em>, and the
/// <c>head</c>, <c>maxp</c>, <c>loca</c> and <c>glyf</c> table chapters — cross-checked against
/// Apple's <em>TrueType Reference Manual</em>.
/// <para/>
/// A font opens with a version, a count of tables, and three numbers a binary search once needed;
/// then one sixteen-byte record a table, each a four-letter tag, a checksum, an offset from the
/// start of the file and a length. <c>head</c> says how many units there are to the em and whether
/// the glyph offsets are stored short or long, <c>maxp</c> how many glyphs there are, <c>loca</c>
/// where each one starts, and <c>glyf</c> holds the outlines.
/// <para/>
/// An outline is contours of points, each either on the curve or off it. Two on-curve points in a
/// row are a straight line; an off-curve point between them is the control point of a quadratic
/// curve, and two off-curve points in a row have an on-curve point implied exactly halfway between
/// them, which is the compression that makes the format what it is. A glyph may instead be a
/// composite, which is other glyphs placed under an offset and a two-by-two transform.
/// <para/>
/// What is drawn is a sheet: the font's first glyphs laid out sixteen to a row at a stated size, in
/// glyph order rather than in any character order, which is what the outlines are stored in. That is
/// a real rendering of what the file holds rather than a picture pulled out of it — the outlines are
/// the file's whole content.
/// <para/>
/// Only fonts with <c>glyf</c> outlines are read: the version has to be <c>0x00010000</c> or the tag
/// <c>true</c>. An <c>OTTO</c> font keeps its outlines in a <c>CFF </c> table as Type 2 charstrings,
/// which is a different language, and a <c>ttcf</c> collection is several fonts in one file; both
/// are refused by name rather than half-read.
/// <para/>
/// It does not write.
/// </remarks>
public readonly record struct TrueTypeFile : IImageFormatReader<TrueTypeFile>, IImageToRawImage<TrueTypeFile> {

  /// <summary>The version a font with TrueType outlines states.</summary>
  public const uint TrueTypeVersion = 0x00010000;

  /// <summary>The tag Apple's own fonts state instead.</summary>
  public const uint AppleTag = 0x74727565;

  /// <summary>The tag of a font whose outlines are Type 2 charstrings rather than glyphs.</summary>
  public const uint OpenTypeCffTag = 0x4F54544F;

  /// <summary>The tag a collection of fonts opens with.</summary>
  public const uint CollectionTag = 0x74746366;

  /// <summary>What <c>head</c> carries at offset twelve, which says the table is the right one.</summary>
  public const uint HeadMagic = 0x5F0F3CF5;

  /// <summary>How many glyphs a sheet shows at most.</summary>
  public const int SheetGlyphs = 256;

  /// <summary>How many glyphs there are to a row.</summary>
  public const int SheetColumns = 16;

  /// <summary>How many pixels tall one cell of the sheet is.</summary>
  public const int SheetCell = 48;

  static string IImageFormatMetadata<TrueTypeFile>.PrimaryExtension => ".ttf";
  static string[] IImageFormatMetadata<TrueTypeFile>.FileExtensions => [".ttf"];
  static TrueTypeFile IImageFormatReader<TrueTypeFile>.FromSpan(ReadOnlySpan<byte> data) => TrueTypeReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<TrueTypeFile>.VideoModes => [
    new("Glyph sheet", [(IntegerRange.Any, IntegerRange.Any)], [2])
  ];

  static bool? IImageFormatMetadata<TrueTypeFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 4)
      return null;

    var version = ((uint)header[0] << 24) | ((uint)header[1] << 16) | ((uint)header[2] << 8) | header[3];

    return version is TrueTypeVersion or AppleTag ? true : null;
  }

  /// <summary>How many units there are to the em, from <c>head</c>.</summary>
  public int UnitsPerEm { get; init; }

  /// <summary>How many glyphs the font holds, from <c>maxp</c>.</summary>
  public int GlyphCount { get; init; }

  /// <summary>Each glyph's outline, already turned into contours of points.</summary>
  public IReadOnlyList<TrueTypeGlyph> Glyphs { get; init; }

  public static RawImage ToRawImage(TrueTypeFile file) => TrueTypeRenderer.Render(file);
}

/// <summary>One point of an outline, and whether the curve passes through it.</summary>
/// <param name="X">Where it is across, in font units.</param>
/// <param name="Y">Where it is up, in font units.</param>
/// <param name="OnCurve">Whether the outline passes through it or only bends towards it.</param>
public readonly record struct TrueTypePoint(double X, double Y, bool OnCurve);

/// <summary>One glyph's outline as closed contours.</summary>
/// <param name="Contours">Each contour's points, in order.</param>
public readonly record struct TrueTypeGlyph(IReadOnlyList<IReadOnlyList<TrueTypePoint>> Contours);
