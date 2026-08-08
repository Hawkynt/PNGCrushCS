namespace FileFormat.Gem;

/// <summary>One recorded VDI call: what was asked for, where, and with what.</summary>
/// <param name="Opcode">The VDI function number.</param>
/// <param name="SubOpcode">The function's sub-identifier, which only a few opcodes use.</param>
/// <param name="Points">The <c>ptsin</c> array, as x and y pairs.</param>
/// <param name="Integers">The <c>intin</c> array.</param>
public readonly record struct GemRecord(int Opcode, int SubOpcode, short[] Points, short[] Integers) {

  /// <summary>How many points the record carries.</summary>
  public int PointCount => this.Points.Length / 2;

  /// <summary>The x of one of the points.</summary>
  public short X(int index) => this.Points[index * 2];

  /// <summary>The y of one of the points.</summary>
  public short Y(int index) => this.Points[index * 2 + 1];
}

/// <summary>The VDI function numbers a metafile can hold.</summary>
/// <remarks>
/// Only the ones that draw or that change how a drawing looks are named. The rest are recorded by
/// number and played by nobody: a metafile can carry any call the interface has, including ones
/// that ask a device a question, and a player is told outright to pass over what it does not know.
/// </remarks>
public static class GemOpcode {

  /// <summary>Device control, whose sub-opcode 99 marks a metafile's own extensions.</summary>
  public const int Escape = 5;

  /// <summary>A run of connected line segments.</summary>
  public const int PolyLine = 6;

  /// <summary>A marker at each of the given points.</summary>
  public const int PolyMarker = 7;

  /// <summary>A string of text.</summary>
  public const int Text = 8;

  /// <summary>A closed polygon, filled.</summary>
  public const int FilledArea = 9;

  /// <summary>A rectangular block of pixels.</summary>
  public const int CellArray = 10;

  /// <summary>One of the generalised primitives, chosen by the sub-opcode.</summary>
  public const int GeneralisedPrimitive = 11;

  /// <summary>Character height.</summary>
  public const int SetTextHeight = 12;

  /// <summary>Character rotation, in tenths of a degree.</summary>
  public const int SetTextRotation = 13;

  /// <summary>Redefines one of the palette entries.</summary>
  public const int SetColour = 14;

  /// <summary>Line style: solid, dashed, dotted and the rest.</summary>
  public const int SetLineType = 15;

  /// <summary>Line width, given as the x of a point.</summary>
  public const int SetLineWidth = 16;

  /// <summary>Line colour, as a palette index.</summary>
  public const int SetLineColour = 17;

  /// <summary>Marker style.</summary>
  public const int SetMarkerType = 18;

  /// <summary>Marker height.</summary>
  public const int SetMarkerHeight = 19;

  /// <summary>Marker colour.</summary>
  public const int SetMarkerColour = 20;

  /// <summary>Text face.</summary>
  public const int SetTextFont = 21;

  /// <summary>Text colour.</summary>
  public const int SetTextColour = 22;

  /// <summary>Fill style: hollow, solid, pattern, hatch or a user-defined one.</summary>
  public const int SetFillInterior = 23;

  /// <summary>Which pattern or hatch within the style.</summary>
  public const int SetFillStyle = 24;

  /// <summary>Fill colour, as a palette index.</summary>
  public const int SetFillColour = 25;

  /// <summary>Writing mode: replace, transparent, exclusive-or or reverse-transparent.</summary>
  public const int SetWritingMode = 32;

  /// <summary>Text alignment.</summary>
  public const int SetTextAlignment = 39;

  /// <summary>Whether a filled area is outlined as well.</summary>
  public const int SetFillPerimeter = 104;

  /// <summary>The user-defined line pattern.</summary>
  public const int SetUserLineStyle = 113;

  /// <summary>The user-defined fill pattern.</summary>
  public const int SetUserFillPattern = 112;

  /// <summary>Line end styles.</summary>
  public const int SetLineEnds = 108;
}

/// <summary>The sub-opcodes of <see cref="GemOpcode.GeneralisedPrimitive"/>.</summary>
public static class GemPrimitive {

  /// <summary>A filled rectangle between two corners.</summary>
  public const int Bar = 1;

  /// <summary>An arc of a circle.</summary>
  public const int Arc = 2;

  /// <summary>A filled sector of a circle.</summary>
  public const int PieSlice = 3;

  /// <summary>A filled circle.</summary>
  public const int Circle = 4;

  /// <summary>A filled ellipse.</summary>
  public const int Ellipse = 5;

  /// <summary>An arc of an ellipse.</summary>
  public const int EllipticalArc = 6;

  /// <summary>A filled sector of an ellipse.</summary>
  public const int EllipticalPie = 7;

  /// <summary>The outline of a rectangle with rounded corners.</summary>
  public const int RoundedBox = 8;

  /// <summary>A filled rectangle with rounded corners.</summary>
  public const int FilledRoundedBox = 9;

  /// <summary>Text stretched to a stated width.</summary>
  public const int JustifiedText = 10;
}
