using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Dxf;

/// <summary>An AutoCAD Drawing Exchange File (.dxf).</summary>
/// <remarks>
/// Built from Autodesk's own DXF Reference: <em>About the General DXF File Structure</em>,
/// <em>Header Section Group Codes</em>, <em>Common Group Codes for Entities</em> and the entity
/// pages for LINE, LWPOLYLINE, POLYLINE, VERTEX, SEQEND, CIRCLE, ARC, ELLIPSE, SOLID, TRACE,
/// 3DFACE, POINT and INSERT, at <c>help.autodesk.com</c> under <c>ENU/AutoCAD-DXF</c>.
/// <para/>
/// The whole file is pairs of lines: an integer group code, then the value it labels. Code 0 names
/// a thing — <c>SECTION</c>, <c>ENDSEC</c>, an entity's type, <c>EOF</c> — code 2 names a section or
/// a block, code 9 names a header variable, and the numeric codes carry the geometry: 10/20/30 is a
/// point, 11/21/31 a second one, 40 a radius or a height, 50 and 51 angles in degrees.
/// <para/>
/// Only the ASCII form is read. The binary form opens with the sentinel
/// <c>AutoCAD Binary DXF</c> and is recognised so it can be refused by name rather than
/// misparsed. The entities drawn are the ones that are geometry on their own: LINE, POINT,
/// LWPOLYLINE and POLYLINE with their bulged arc segments, CIRCLE, ARC, ELLIPSE, SOLID and TRACE
/// filled, 3DFACE outlined, and INSERT, which places a block from the BLOCKS section under its own
/// scale and rotation. Everything projects onto the world xy plane; a drawing that is genuinely
/// three-dimensional comes out as its plan.
/// <para/>
/// The size is the one the file states: <c>$EXTMIN</c> and <c>$EXTMAX</c> from the HEADER section,
/// which are the corners of the drawing's extents in world coordinates. AutoCAD writes
/// <c>1.0E+20</c> into those variables for a drawing whose extents have never been computed, so a
/// pair that is not a real box is passed over and the box the geometry actually falls in is used
/// instead — otherwise there would be nothing to fit into.
/// <para/>
/// Text is not drawn. TEXT and MTEXT name a text style, and a style names a font file that is not in
/// the drawing, so the glyphs are not there to draw; the same holds for the SVG and HP-GL readers
/// here and for the same reason. A drawing that is nothing but annotation therefore comes out empty
/// rather than wrong.
/// <para/>
/// Colour is the AutoCAD Color Index, resolved through the LAYER table where an entity says
/// BYLAYER. Only indices 1 to 9 have colours that the DXF Reference itself fixes, so anything
/// outside that range is drawn in black rather than guessed at.
/// <para/>
/// It does not write.
/// </remarks>
public readonly record struct DxfFile : IImageFormatReader<DxfFile>, IImageToRawImage<DxfFile> {

  /// <summary>The sentinel a binary DXF file opens with, which this reader refuses.</summary>
  public const string BinarySentinel = "AutoCAD Binary DXF";

  static string IImageFormatMetadata<DxfFile>.PrimaryExtension => ".dxf";
  static string[] IImageFormatMetadata<DxfFile>.FileExtensions => [".dxf"];
  static DxfFile IImageFormatReader<DxfFile>.FromSpan(ReadOnlySpan<byte> data) => DxfReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<DxfFile>.VideoModes => [
    new("Drawing", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>Every group code and value in the file, in order.</summary>
  public IReadOnlyList<DxfPair> Pairs { get; init; }

  public static RawImage ToRawImage(DxfFile file) => DxfRenderer.Render(file);
}

/// <summary>One group code and the value that follows it.</summary>
/// <param name="Code">The group code, which says what the value means.</param>
/// <param name="Value">The value, as it was written.</param>
public readonly record struct DxfPair(int Code, string Value);
