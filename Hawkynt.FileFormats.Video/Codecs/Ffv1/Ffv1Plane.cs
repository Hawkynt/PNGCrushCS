using System;

namespace FileFormat.Codecs.Ffv1;

/// <summary>
/// One plane of a frame, with the neighbourhood rules a sample's context is built from.
/// </summary>
/// <remarks>
/// The rules matter more than the storage. A sample at the left edge of a slice is predicted from a
/// column that is not there, and the specification says what that column holds: the slice's own
/// leftmost column, moved down one row. So the first sample of a row is predicted from the first
/// sample of the row above it, and not from nothing and not from the end of the previous row.
/// Everything further left, and everything above the first two rows, is zero, and everything to the
/// right of the last column is that column repeated.
/// </remarks>
internal sealed class Ffv1Plane {

  private readonly int[] _samples;

  internal Ffv1Plane(int width, int height) {
    this.Width = width;
    this.Height = height;
    this._samples = new int[width * height];
  }

  internal int Width { get; }
  internal int Height { get; }
  internal int[] Samples => this._samples;

  internal int this[int x, int y] {
    get => this._samples[y * this.Width + x];
    set => this._samples[y * this.Width + x] = value;
  }

  /// <summary>A sample at any position, real or off the edge, as the border rules define it.</summary>
  internal int At(int x, int y) {
    if (y < 0)
      return 0;

    if (x >= this.Width)
      x = this.Width - 1;

    if (x >= 0)
      return this._samples[y * this.Width + x];

    // The column to the left of the slice is the slice's first column one row down; anything
    // further left than that is nothing at all.
    return x == -1 && y > 0 ? this._samples[(y - 1) * this.Width] : 0;
  }
}
