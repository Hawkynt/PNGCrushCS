using System;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.Core.Vector;

namespace FileFormat.Cgm;

/// <summary>Everything a metafile sets that changes how the commands after it are read or drawn.</summary>
/// <remarks>
/// The defaults are the standard's own, and they matter more here than in most formats: a file that
/// never states its integer precision is not saying it does not care, it is saying sixteen bits,
/// and reading it at any other width desynchronises the stream from that point on.
/// </remarks>
public sealed class CgmState {

  /// <summary>Bits in an ordinary integer.</summary>
  public int IntegerPrecision { get; set; } = 16;

  /// <summary>Bits in an index.</summary>
  public int IndexPrecision { get; set; } = 16;

  /// <summary>Bits in a colour index.</summary>
  public int ColourIndexPrecision { get; set; } = 8;

  /// <summary>Bits in one component of a directly stated colour.</summary>
  public int ColourPrecision { get; set; } = 8;

  /// <summary>Whether a real is floating point rather than fixed.</summary>
  public bool RealIsFloating { get; set; }

  /// <summary>Bits in a real's whole part, or its exponent field when it is floating.</summary>
  public int RealWhole { get; set; } = 16;

  /// <summary>Bits in a real's fraction.</summary>
  public int RealFraction { get; set; } = 16;

  /// <summary>Whether the picture's own coordinates are integers rather than reals.</summary>
  public bool VdcIsInteger { get; set; } = true;

  /// <summary>Bits in an integer coordinate.</summary>
  public int VdcIntegerPrecision { get; set; } = 16;

  /// <summary>Whether a real coordinate is floating point rather than fixed.</summary>
  public bool VdcRealIsFloating { get; set; }

  /// <summary>Bits in a real coordinate's whole part.</summary>
  public int VdcRealWhole { get; set; } = 16;

  /// <summary>Bits in a real coordinate's fraction.</summary>
  public int VdcRealFraction { get; set; } = 16;

  /// <summary>Whether colours are stated as components rather than as indices into the table.</summary>
  public bool DirectColour { get; set; }

  /// <summary>What value a colour component reaches white at, per channel.</summary>
  public int[] ColourMaximum { get; set; } = [255, 255, 255];

  /// <summary>What value a colour component starts from, per channel.</summary>
  public int[] ColourMinimum { get; set; } = [0, 0, 0];

  /// <summary>The colour each index names.</summary>
  public Dictionary<int, Rgba32> ColourTable { get; } = [];

  /// <summary>The colour a line is drawn in.</summary>
  public Rgba32 LineColour { get; set; } = Rgba32.Black;

  /// <summary>The colour a filled area is painted in.</summary>
  public Rgba32 FillColour { get; set; } = Rgba32.Black;

  /// <summary>The colour a filled area's outline is drawn in.</summary>
  public Rgba32 EdgeColour { get; set; } = Rgba32.Black;

  /// <summary>How wide a line is, in whatever the line width specification mode says.</summary>
  public double LineWidth { get; set; } = 1;

  /// <summary>How wide an edge is.</summary>
  public double EdgeWidth { get; set; } = 1;

  /// <summary>Whether line widths are stated in the picture's own units rather than as a scale.</summary>
  public bool LineWidthIsAbsolute { get; set; }

  /// <summary>Whether edge widths are stated in the picture's own units rather than as a scale.</summary>
  public bool EdgeWidthIsAbsolute { get; set; }

  /// <summary>How a filled area is painted: hollow, solid, a pattern, a hatch or empty.</summary>
  public int InteriorStyle { get; set; }

  /// <summary>Which hatch, when the interior style is hatched.</summary>
  public int HatchIndex { get; set; } = 1;

  /// <summary>Whether a filled area is outlined.</summary>
  public bool EdgeVisible { get; set; }

  /// <summary>Which of the standard's dash patterns a line uses.</summary>
  public int LineType { get; set; } = 1;

  /// <summary>Which of the standard's dash patterns an edge uses.</summary>
  public int EdgeType { get; set; } = 1;

  /// <summary>The picture's own extent, which is the size it is drawn at.</summary>
  public (double X1, double Y1, double X2, double Y2) VdcExtent { get; set; } = (0, 0, 32767, 32767);

  /// <summary>What the background is, where the file states one.</summary>
  public Rgba32 Background { get; set; } = Rgba32.White;

  /// <summary>Interior styles, as the standard numbers them.</summary>
  public const int InteriorHollow = 0, InteriorSolid = 1, InteriorPattern = 2, InteriorHatch = 3, InteriorEmpty = 4;

  /// <summary>The colour an index names, or black when the file never defined it.</summary>
  public Rgba32 Lookup(int index) => this.ColourTable.TryGetValue(index, out var colour) ? colour : index == 0 ? Rgba32.White : Rgba32.Black;

  /// <summary>One component scaled from the file's own range onto a byte.</summary>
  public byte Component(int value, int channel) {
    var low = channel < this.ColourMinimum.Length ? this.ColourMinimum[channel] : 0;
    var high = channel < this.ColourMaximum.Length ? this.ColourMaximum[channel] : 255;
    if (high <= low)
      return (byte)Math.Clamp(value, 0, 255);

    return (byte)Math.Clamp((int)Math.Round((value - low) * 255.0 / (high - low)), 0, 255);
  }

  /// <summary>The six hatches the standard defines, as sixteen-bit rows.</summary>
  /// <remarks>
  /// Horizontal, vertical, the two diagonals, and the two crosses — the standard names them but
  /// leaves how finely they are drawn to the device, so these are drawn at the pixel grid.
  /// </remarks>
  public static VectorStipple Hatch(int index) => index switch {
    1 => new([0xFFFF, 0, 0, 0, 0, 0, 0, 0]),
    2 => new([0x8080]),
    3 => new([0x0101, 0x0202, 0x0404, 0x0808, 0x1010, 0x2020, 0x4040, 0x8080]),
    4 => new([0x8080, 0x4040, 0x2020, 0x1010, 0x0808, 0x0404, 0x0202, 0x0101]),
    5 => new([0xFFFF, 0x8080, 0x8080, 0x8080, 0x8080, 0x8080, 0x8080, 0x8080]),
    6 => new([0x8181, 0x4242, 0x2424, 0x1818, 0x1818, 0x2424, 0x4242, 0x8181]),
    _ => new([0xFFFF])
  };

  /// <summary>
  /// The dash runs a line type selects, in the same units as the width given.
  /// </summary>
  /// <remarks>
  /// Types one to five are the standard's own: solid, dashed, dotted, dash-dot and dash-dot-dot.
  /// Their lengths are left to the device, so they are drawn as multiples of the line's own width,
  /// which keeps a dash visible whatever the picture is rendered at.
  /// </remarks>
  public static double[] Dashes(int lineType, double unit) => lineType switch {
    2 => [4 * unit, 3 * unit],
    3 => [unit, 2 * unit],
    4 => [5 * unit, 2 * unit, unit, 2 * unit],
    5 => [5 * unit, 2 * unit, unit, 2 * unit, unit, 2 * unit],
    _ => []
  };
}
