using System;

namespace FileFormat.Core;

/// <summary>A rectangle measured in whole pixels.</summary>
/// <remarks>
/// The framework's own <c>Rectangle</c> lives in <c>System.Drawing</c>, which carries a native
/// dependency that only exists on Windows. Naming a region of a picture should not cost that.
/// </remarks>
public readonly record struct PixelRect(int X, int Y, int Width, int Height) {

  /// <summary>One past the rightmost column.</summary>
  public int Right => this.X + this.Width;

  /// <summary>One past the bottom row.</summary>
  public int Bottom => this.Y + this.Height;

  /// <summary>Whether the rectangle covers nothing.</summary>
  public bool IsEmpty => this.Width <= 0 || this.Height <= 0;

  /// <summary>The part of this rectangle that lies inside a picture of the given size.</summary>
  public PixelRect ClampTo(int width, int height) {
    var left = Math.Max(0, this.X);
    var top = Math.Max(0, this.Y);
    var right = Math.Min(width, this.Right);
    var bottom = Math.Min(height, this.Bottom);

    return new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
  }

  public override string ToString() => $"{this.Width}x{this.Height}+{this.X}+{this.Y}";
}
