using System;
using System.Globalization;

namespace FileFormat.Core.Vector;

/// <summary>An affine transform, written the way every drawing format writes one.</summary>
/// <remarks>
/// Six numbers in the order PostScript, SVG, CGM and Xara all use: <c>a b c d e f</c> mapping
/// <c>(x, y)</c> to <c>(a·x + c·y + e, b·x + d·y + f)</c>. Keeping the same order as the files
/// means a matrix read out of one of them needs no rearranging, which is where sign errors come
/// from.
/// </remarks>
public readonly record struct Matrix2D(double A, double B, double C, double D, double E, double F) {

  /// <summary>The transform that changes nothing.</summary>
  public static Matrix2D Identity => new(1, 0, 0, 1, 0, 0);

  /// <summary>A move by the given amount.</summary>
  public static Matrix2D Translation(double x, double y) => new(1, 0, 0, 1, x, y);

  /// <summary>A scale about the origin.</summary>
  public static Matrix2D Scaling(double x, double y) => new(x, 0, 0, y, 0, 0);

  /// <summary>A rotation about the origin, anticlockwise, in radians.</summary>
  public static Matrix2D Rotation(double radians) {
    var (sin, cos) = Math.SinCos(radians);
    return new(cos, sin, -sin, cos, 0, 0);
  }

  /// <summary>A skew along x, in radians.</summary>
  public static Matrix2D SkewX(double radians) => new(1, 0, Math.Tan(radians), 1, 0, 0);

  /// <summary>A skew along y, in radians.</summary>
  public static Matrix2D SkewY(double radians) => new(1, Math.Tan(radians), 0, 1, 0, 0);

  /// <summary>This transform followed by <paramref name="other"/>.</summary>
  /// <remarks>
  /// The order is the one nesting needs: a child's own transform composed onto its parent's gives
  /// <c>parent.Then(child)</c> read left to right, which is the order the file lists them in.
  /// </remarks>
  public Matrix2D Then(Matrix2D other) => new(
    this.A * other.A + this.B * other.C,
    this.A * other.B + this.B * other.D,
    this.C * other.A + this.D * other.C,
    this.C * other.B + this.D * other.D,
    this.E * other.A + this.F * other.C + other.E,
    this.E * other.B + this.F * other.D + other.F
  );

  /// <summary>Maps a point.</summary>
  public (double X, double Y) Apply(double x, double y) => (this.A * x + this.C * y + this.E, this.B * x + this.D * y + this.F);

  /// <summary>Maps a direction, which is a point with the translation left off.</summary>
  public (double X, double Y) ApplyVector(double x, double y) => (this.A * x + this.C * y, this.B * x + this.D * y);

  /// <summary>How much the transform multiplies area by.</summary>
  public double Determinant => this.A * this.D - this.B * this.C;

  /// <summary>The transform that undoes this one, or null where it collapses the plane.</summary>
  /// <remarks>
  /// What a raster needs: a picture is placed by mapping its own pixel grid onto the page, and
  /// drawing it means asking, for each pixel of the page, which pixel of the picture landed there.
  /// That is the inverse, and going the other way — walking the source and marking where each pixel
  /// goes — leaves gaps wherever the transform enlarges.
  /// </remarks>
  public Matrix2D? Inverse {
    get {
      var determinant = this.Determinant;
      if (Math.Abs(determinant) < 1e-12)
        return null;

      var scale = 1 / determinant;
      return new(
        this.D * scale,
        -this.B * scale,
        -this.C * scale,
        this.A * scale,
        (this.C * this.F - this.D * this.E) * scale,
        (this.B * this.E - this.A * this.F) * scale
      );
    }
  }

  /// <summary>
  /// The factor a length is multiplied by on average, which is what a stroke width has to be scaled
  /// by when the transform is not a plain scale.
  /// </summary>
  /// <remarks>
  /// The root of the absolute determinant. A stroke under a non-uniform transform is properly an
  /// ellipse swept along the path and no single number describes it; every renderer picks one, and
  /// this is the one that keeps the stroked area right.
  /// </remarks>
  public double MeanScale => Math.Sqrt(Math.Abs(this.Determinant));

  public override string ToString()
    => string.Format(CultureInfo.InvariantCulture, "[{0} {1} {2} {3} {4} {5}]", this.A, this.B, this.C, this.D, this.E, this.F);
}
