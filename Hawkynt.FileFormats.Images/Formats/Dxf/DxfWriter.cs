using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Dxf;

/// <summary>Writes the ASCII form of an AutoCAD Drawing Exchange File.</summary>
/// <remarks>
/// Autodesk's <em>Writing a DXF File</em> says a usable file may omit sections it does not need,
/// while <em>About the General DXF File Structure</em> defines the alternating group-code/value
/// lines and the SECTION/ENDSEC/EOF framing. Existing files are therefore serialised directly from
/// their pairs rather than normalised into a larger AutoCAD database skeleton.
/// <para/>
/// A raster converted to DXF is represented by filled SOLID entities. Adjacent equal pixels are
/// coalesced horizontally and then vertically, so flat areas become rectangles rather than one
/// entity per pixel. Group 420 stores each rectangle's true colour as the 0x00RRGGBB value the
/// reference defines. Fully white rectangles are omitted because the renderer's paper is white;
/// source alpha is composited onto the same white paper before the RGB value is written.
/// </remarks>
public static class DxfWriter {

  /// <summary>The first group code Autodesk reserves for DXF data.</summary>
  private const int _MinGroupCode = -5;

  /// <summary>The last group code Autodesk currently defines.</summary>
  private const int _MaxGroupCode = 1071;

  /// <summary>
  /// The renderer refuses more shapes than this too. Keeping the same bound means a picture written
  /// here remains one this package can read back instead of producing a file it refuses itself.
  /// </summary>
  private const int _MaxSolids = 1 << 20;

  private const int _White = 0x00ffffff;

  private readonly record struct Run(int Left, int Right, int Colour);

  private readonly record struct Rectangle(int Left, int Right, int Bottom, int Top, int Colour);

  /// <summary>Serialises all group-code/value pairs with canonical CRLF line endings.</summary>
  public static byte[] ToBytes(DxfFile file) {
    var pairs = file.Pairs ?? throw new ArgumentException("No DXF group codes to write.", nameof(file));
    if (pairs.Count == 0)
      throw new ArgumentException("A DXF file cannot contain no group codes.", nameof(file));

    using var stream = new MemoryStream();
    using (var writer = new StreamWriter(stream, Encoding.Latin1, 4096, true) { NewLine = "\r\n" }) {
      foreach (var pair in pairs) {
        if (pair.Code is < _MinGroupCode or > _MaxGroupCode)
          throw new ArgumentException($"DXF group code {pair.Code} is outside {_MinGroupCode} to {_MaxGroupCode}.", nameof(file));

        var value = pair.Value ?? throw new ArgumentException($"DXF group code {pair.Code} has no value.", nameof(file));
        if (value.AsSpan().IndexOfAny('\r', '\n') >= 0)
          throw new ArgumentException($"DXF group code {pair.Code} contains a line break inside its value.", nameof(file));

        foreach (var character in value)
          if (character > byte.MaxValue)
            throw new ArgumentException(
              $"DXF group code {pair.Code} contains U+{(int)character:X4}, which cannot be represented by this reader's single-byte ASCII-DXF model.",
              nameof(file)
            );

        writer.Write(pair.Code.ToString(CultureInfo.InvariantCulture).PadLeft(3));
        writer.WriteLine();
        writer.Write(value);
        writer.WriteLine();
      }
    }

    return stream.ToArray();
  }

  /// <summary>Builds a self-contained DXF drawing whose filled rectangles reproduce a raster.</summary>
  public static DxfFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentOutOfRangeException(nameof(image), $"A DXF drawing cannot represent an image of {image.Width} by {image.Height} pixels.");

    if (!image.HasEnoughPixelData)
      throw new ArgumentException("The source image does not carry enough pixel data for its stated size and format.", nameof(image));

    var rgba = image.ToRgba32();
    var pairs = new List<DxfPair> {
      new(0, "SECTION"), new(2, "HEADER"),
      new(9, "$ACADVER"), new(1, "AC1018"),
      new(9, "$EXTMIN"), new(10, "0"), new(20, "0"), new(30, "0"),
      new(9, "$EXTMAX"), new(10, _Text(image.Width)), new(20, _Text(image.Height)), new(30, "0"),
      new(0, "ENDSEC"),
      new(0, "SECTION"), new(2, "ENTITIES")
    };

    Dictionary<Run, Rectangle> active = [];
    var solids = 0;

    for (var row = 0; row < image.Height; ++row) {
      Dictionary<Run, Rectangle> current = [];
      var x = 0;
      while (x < image.Width) {
        var colour = _Colour(rgba, row * image.Width + x);
        var right = x + 1;
        while (right < image.Width && _Colour(rgba, row * image.Width + right) == colour)
          ++right;

        if (colour != _White) {
          var run = new Run(x, right, colour);
          var bottom = image.Height - row - 1;
          if (active.Remove(run, out var rectangle))
            current.Add(run, rectangle with { Bottom = bottom });
          else
            current.Add(run, new(x, right, bottom, bottom + 1, colour));
        }

        x = right;
      }

      foreach (var rectangle in active.Values)
        _AddSolid(pairs, rectangle, ref solids);

      active = current;
    }

    foreach (var rectangle in active.Values)
      _AddSolid(pairs, rectangle, ref solids);

    pairs.Add(new(0, "ENDSEC"));
    pairs.Add(new(0, "EOF"));

    return new() { Pairs = pairs };
  }

  /// <summary>Composites one RGBA pixel onto the white paper a DXF drawing is rendered on.</summary>
  private static int _Colour(ReadOnlySpan<byte> rgba, int pixel) {
    var at = checked(pixel * 4);
    var alpha = rgba[at + 3];
    if (alpha == byte.MaxValue)
      return rgba[at] << 16 | rgba[at + 1] << 8 | rgba[at + 2];

    if (alpha == 0)
      return _White;

    var inverse = byte.MaxValue - alpha;
    var red = (rgba[at] * alpha + byte.MaxValue * inverse + 127) / byte.MaxValue;
    var green = (rgba[at + 1] * alpha + byte.MaxValue * inverse + 127) / byte.MaxValue;
    var blue = (rgba[at + 2] * alpha + byte.MaxValue * inverse + 127) / byte.MaxValue;
    return red << 16 | green << 8 | blue;
  }

  /// <summary>Adds one filled rectangle using the corner order the SOLID entity defines.</summary>
  private static void _AddSolid(List<DxfPair> pairs, Rectangle rectangle, ref int solids) {
    if (++solids > _MaxSolids)
      throw new InvalidDataException(
        $"This raster needs more than {_MaxSolids} DXF SOLID entities after run coalescing; refusing a drawing this package would not read back."
      );

    var left = _Text(rectangle.Left);
    var right = _Text(rectangle.Right);
    var bottom = _Text(rectangle.Bottom);
    var top = _Text(rectangle.Top);

    pairs.AddRange([
      new(0, "SOLID"),
      new(8, "0"),
      new(420, _Text(rectangle.Colour)),
      new(10, left), new(20, bottom),
      new(11, right), new(21, bottom),
      new(12, left), new(22, top),
      new(13, right), new(23, top)
    ]);
  }

  private static string _Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
