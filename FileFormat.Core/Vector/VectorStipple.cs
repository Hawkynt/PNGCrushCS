using System;

namespace FileFormat.Core.Vector;

/// <summary>
/// A repeating dot pattern a fill only reaches through, sixteen pixels wide and as many rows tall
/// as the pattern has.
/// </summary>
/// <remarks>
/// The machines these formats come from could not fill in shades, so they filled in patterns: a
/// small bitmap tiled across the screen and used as a mask. GEM's twenty-four fill patterns and
/// twelve hatches are that, and so are CGM's hatch styles and HP-GL's fill types. Sixteen bits wide
/// is not a choice — it is the width every one of those tables is written at.
/// <para/>
/// The tile lines up with the pixel grid rather than with the shape, because that is what the
/// original did: the pattern belonged to the screen, so two shapes filled with the same one met
/// without a seam.
/// </remarks>
public readonly record struct VectorStipple {

  /// <summary>How wide every one of these tables is.</summary>
  public const int TileWidth = 16;

  private readonly ushort[]? _rows;

  /// <summary>Builds a stipple from its rows, most significant bit leftmost.</summary>
  public VectorStipple(ReadOnlySpan<ushort> rows) {
    if (rows.Length == 0)
      throw new ArgumentOutOfRangeException(nameof(rows), "A stipple needs at least one row.");

    this._rows = rows.ToArray();
  }

  /// <summary>How many rows before the pattern repeats.</summary>
  public int Height => this._rows?.Length ?? 1;

  /// <summary>Whether the pattern is entirely holes, so a fill through it paints nothing.</summary>
  public bool IsBlank {
    get {
      if (this._rows == null)
        return true;

      foreach (var row in this._rows)
        if (row != 0)
          return false;

      return true;
    }
  }

  /// <summary>Whether the pattern lets paint through at the given pixel.</summary>
  public bool Covers(int x, int y) {
    if (this._rows == null)
      return false;

    var row = this._rows[((y % this._rows.Length) + this._rows.Length) % this._rows.Length];
    var bit = ((x % TileWidth) + TileWidth) % TileWidth;
    return (row & (1 << (TileWidth - 1 - bit))) != 0;
  }

  /// <summary>A pattern that lets everything through, which is a solid fill.</summary>
  public static VectorStipple Solid => new([0xFFFF]);
}
