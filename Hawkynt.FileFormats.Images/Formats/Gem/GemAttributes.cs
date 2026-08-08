using System;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.Core.Vector;

namespace FileFormat.Gem;

/// <summary>The VDI's own colours, fill patterns and line styles.</summary>
/// <remarks>
/// A metafile names a colour by an index and a fill by a table entry, and neither is in the file:
/// they are the device's, and the device is the VDI. The tables here are the ones the VDI ships
/// with, taken from EmuTOS, which is the interface rather than a description of it — the same
/// sixteen-bit rows the original ROM holds, in the same order and selected the same way.
/// </remarks>
public static class GemAttributes {

  /// <summary>The sixteen colours a VDI workstation opens with, by pen number.</summary>
  /// <remarks>
  /// Pen 0 is white and pen 1 is black, which is the opposite way round from most palettes and is
  /// deliberate: 0 is the paper and 1 is the ink. After the six saturated primaries come two greys
  /// and then the six pale forms. The values are the interface's own, stated there in parts per
  /// thousand — 714 and 428 for the greys, and 428 for the pale channels — scaled to eight bits.
  /// </remarks>
  public static readonly Rgba32[] Palette = [
    new(255, 255, 255), // 0  white — the paper
    new(0, 0, 0),       // 1  black — the ink
    new(255, 0, 0),     // 2  red
    new(0, 255, 0),     // 3  green
    new(0, 0, 255),     // 4  blue
    new(0, 255, 255),   // 5  cyan
    new(255, 255, 0),   // 6  yellow
    new(255, 0, 255),   // 7  magenta
    new(182, 182, 182), // 8  light grey
    new(109, 109, 109), // 9  grey
    new(255, 109, 109), // 10 light red
    new(109, 255, 109), // 11 light green
    new(109, 109, 255), // 12 light blue
    new(109, 255, 255), // 13 light cyan
    new(255, 255, 109), // 14 light yellow
    new(255, 109, 255)  // 15 light magenta
  ];

  /// <summary>Fill styles, as <c>vsf_interior</c> names them.</summary>
  public const int InteriorHollow = 0, InteriorSolid = 1, InteriorPattern = 2, InteriorHatch = 3, InteriorUser = 4;

  /// <summary>The eight dithers, four rows each, that <c>vsf_style</c> 1 to 8 select under a pattern fill.</summary>
  private static readonly ushort[] _Dither = [
    0x0000, 0x4444, 0x0000, 0x1111,
    0x0000, 0x5555, 0x0000, 0x5555,
    0x8888, 0x5555, 0x2222, 0x5555,
    0xAAAA, 0x5555, 0xAAAA, 0x5555,
    0xAAAA, 0xDDDD, 0xAAAA, 0x7777,
    0xAAAA, 0xFFFF, 0xAAAA, 0xFFFF,
    0xEEEE, 0xFFFF, 0xBBBB, 0xFFFF,
    0xFFFF, 0xFFFF, 0xFFFF, 0xFFFF
  ];

  /// <summary>The sixteen named patterns, eight rows each, that <c>vsf_style</c> 9 upwards select.</summary>
  private static readonly ushort[] _Patterns = [
    0xFFFF, 0x8080, 0x8080, 0x8080, 0xFFFF, 0x0808, 0x0808, 0x0808, // brick
    0x2020, 0x4040, 0x8080, 0x4141, 0x2222, 0x1414, 0x0808, 0x1010, // diagonal bricks
    0x0000, 0x0000, 0x1010, 0x2828, 0x0000, 0x0000, 0x0101, 0x8282, // grass
    0x0202, 0x0202, 0xAAAA, 0x5050, 0x2020, 0x2020, 0xAAAA, 0x0505, // trees
    0x4040, 0x8080, 0x0000, 0x0808, 0x0404, 0x0202, 0x0000, 0x2020, // dashed crosses
    0x6606, 0xC6C6, 0xD8D8, 0x1818, 0x8181, 0x8DB1, 0x0C33, 0x6000, // cobbles
    0x0000, 0x0000, 0x0400, 0x0000, 0x0010, 0x0000, 0x8000, 0x0000, // sand
    0xF8F8, 0x6C6C, 0xC6C6, 0x8F8F, 0x1F1F, 0x3636, 0x6363, 0xF1F1, // rough weave
    0xAAAA, 0x0000, 0x8888, 0x1414, 0x2222, 0x4141, 0x8888, 0x0000, // quilt
    0x0808, 0x0000, 0xAAAA, 0x0000, 0x0808, 0x0000, 0x8888, 0x0000, // patterned cross
    0x7777, 0x9898, 0xF8F8, 0xF8F8, 0x7777, 0x8989, 0x8F8F, 0x8F8F, // balls
    0x8080, 0x8080, 0x4141, 0x3E3E, 0x0808, 0x0808, 0x1414, 0xE3E3, // vertical scales
    0x8181, 0x4242, 0x2424, 0x1818, 0x0606, 0x0101, 0x8080, 0x8080, // diagonal scales
    0xF0F0, 0xF0F0, 0xF0F0, 0xF0F0, 0x0F0F, 0x0F0F, 0x0F0F, 0x0F0F, // checkerboard
    0x0808, 0x1C1C, 0x3E3E, 0x7F7F, 0xFFFF, 0x7F7F, 0x3E3E, 0x1C1C, // filled diamond
    0x1111, 0x2222, 0x4444, 0xFFFF, 0x8888, 0x4444, 0x2222, 0xFFFF  // herringbone
  ];

  /// <summary>The six close hatches, eight rows each.</summary>
  private static readonly ushort[] _CloseHatches = [
    0x0101, 0x0202, 0x0404, 0x0808, 0x1010, 0x2020, 0x4040, 0x8080, // narrow +45
    0x6060, 0xC0C0, 0x8181, 0x0303, 0x0606, 0x0C0C, 0x1818, 0x3030, // medium thick 45
    0x4242, 0x8181, 0x8181, 0x4242, 0x2424, 0x1818, 0x1818, 0x2424, // medium crossed 45
    0x8080, 0x8080, 0x8080, 0x8080, 0x8080, 0x8080, 0x8080, 0x8080, // medium vertical
    0xFFFF, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, // medium horizontal
    0xFFFF, 0x8080, 0x8080, 0x8080, 0x8080, 0x8080, 0x8080, 0x8080  // medium cross
  ];

  /// <summary>The six wide hatches, sixteen rows each.</summary>
  private static readonly ushort[] _WideHatches = [
    0x0001, 0x0002, 0x0004, 0x0008, 0x0010, 0x0020, 0x0040, 0x0080,
    0x0100, 0x0200, 0x0400, 0x0800, 0x1000, 0x2000, 0x4000, 0x8000, // wide +45
    0x8003, 0x0007, 0x000E, 0x001C, 0x0038, 0x0070, 0x00E0, 0x01C0,
    0x0380, 0x0700, 0x0E00, 0x1C00, 0x3800, 0x7000, 0xE000, 0xC001, // wide thick 45
    0x8001, 0x4002, 0x2004, 0x1008, 0x0810, 0x0420, 0x0240, 0x0180,
    0x0180, 0x0240, 0x0420, 0x0810, 0x1008, 0x2004, 0x4002, 0x8001, // wide crossed 45
    0x8000, 0x8000, 0x8000, 0x8000, 0x8000, 0x8000, 0x8000, 0x8000,
    0x8000, 0x8000, 0x8000, 0x8000, 0x8000, 0x8000, 0x8000, 0x8000, // wide vertical
    0xFFFF, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
    0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, // wide horizontal
    0xFFFF, 0x8080, 0x8080, 0x8080, 0x8080, 0x8080, 0x8080, 0x8080,
    0xFFFF, 0x8080, 0x8080, 0x8080, 0x8080, 0x8080, 0x8080, 0x8080  // wide cross
  ];

  /// <summary>The colour a palette index names, with anything outside the table taken as ink.</summary>
  public static Rgba32 Colour(int index) => index >= 0 && index < Palette.Length ? Palette[index] : Palette[1];

  /// <summary>
  /// The pattern a fill style and index select, or null when the fill is solid.
  /// </summary>
  /// <param name="interior">The <c>vsf_interior</c> style.</param>
  /// <param name="style">The <c>vsf_style</c> index, which is one-based as the call takes it.</param>
  public static VectorStipple? Stipple(int interior, int style) {
    // The interface stores the index one below what the call was given, and clamps it there.
    var index = Math.Max(style - 1, 0);

    switch (interior) {
      case InteriorSolid:
        return null;
      case InteriorPattern:
        return index < 8
          ? _Slice(_Dither, index * 4, 4)
          : _Slice(_Patterns, Math.Min(index - 8, 15) * 8, 8);
      case InteriorHatch:
        return index < 6
          ? _Slice(_CloseHatches, index * 8, 8)
          : _Slice(_WideHatches, Math.Min(index - 6, 5) * 16, 16);
      default:
        // Hollow paints nothing, and a user-defined pattern the file never sent is nothing either.
        return new VectorStipple([0]);
    }
  }

  /// <summary>The six line styles, as the sixteen-bit masks the interface plays them out from.</summary>
  /// <remarks>
  /// One bit per pixel along the line, most significant first, repeating. Style 1 is solid, and
  /// the other five are the ones <c>vsl_type</c> numbers 2 to 6.
  /// </remarks>
  private static readonly ushort[] _LineStyles = [0xFFFF, 0xFFF0, 0xC0C0, 0xFF18, 0xFF00, 0xF191];

  /// <summary>
  /// The dash pattern a line type selects, as alternating on and off runs, or empty for solid.
  /// </summary>
  /// <param name="lineType">The <c>vsl_type</c> style, which is one-based as the call takes it.</param>
  /// <param name="unit">
  /// How long one bit of the mask is. The interface means one device pixel by it, so a drawing
  /// rendered at more than a 1985 screen's resolution wants the dashes scaled with it rather than
  /// left at a size the picture has outgrown.
  /// </param>
  public static double[] Dashes(int lineType, double unit) {
    var index = lineType - 1;
    if (index <= 0 || index >= _LineStyles.Length || unit <= 0)
      return [];

    var mask = _LineStyles[index];

    // The mask repeats, so where it is read from is a choice. Starting where an off bit turns on
    // makes the runs come out on first and alternating, which is what a dash pattern is; a cyclic
    // string of bits always has an even number of runs, so the pattern closes on itself.
    var start = -1;
    for (var i = 0; i < VectorStipple.TileWidth; ++i)
      if (_Bit(mask, i) && !_Bit(mask, (i + VectorStipple.TileWidth - 1) % VectorStipple.TileWidth)) {
        start = i;
        break;
      }

    // Every bit the same: all on is a solid line, and all off is one the interface never defines.
    if (start < 0)
      return [];

    var runs = new List<double>();
    var current = true;
    var length = 0;
    for (var i = 0; i < VectorStipple.TileWidth; ++i) {
      var bit = _Bit(mask, (start + i) % VectorStipple.TileWidth);
      if (bit != current) {
        runs.Add(length * unit);
        current = bit;
        length = 0;
      }

      ++length;
    }

    runs.Add(length * unit);
    return runs.ToArray();
  }

  private static bool _Bit(ushort mask, int index) => (mask & (1 << (VectorStipple.TileWidth - 1 - index))) != 0;

  private static VectorStipple _Slice(ushort[] table, int from, int count) => new(table.AsSpan(from, count));
}
