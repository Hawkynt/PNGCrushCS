using System;

namespace FileFormat.Codecs.Vp8;

/// <summary>
/// A motion vector, in eighths of a luma pixel.
/// </summary>
/// <remarks>
/// Eighths and not quarters, although the bitstream writes quarters. RFC 6386 section 18.1 doubles
/// every component as it is read, so that luma and chroma vectors — chroma being half the resolution
/// and therefore naturally at eighth-pixel precision — can be handled by one piece of arithmetic.
/// A luma vector is therefore always even, which is why the odd entries of the sub-pixel filter
/// table are only ever reached by chroma.
/// <para/>
/// Both components live in one integer because VP8 compares vectors for equality constantly — the
/// census that picks the mode probabilities, the context that picks the subblock mode probabilities,
/// and the test for a whole-pixel vector all do it — and RFC 6386 spells those comparisons as a
/// comparison of the two components at once.
/// <para/>
/// The components are sixteen bits, as they are in the reference decoder. A frame wide enough for
/// the clamping bounds of section 16.3 to exceed that range would see them wrap, which is the
/// behaviour of every implementation of this format and so is the behaviour of the format.
/// </remarks>
internal readonly struct Vp8MotionVector : IEquatable<Vp8MotionVector> {

  private readonly int _packed;

  /// <summary>
  /// Packs a vector, narrowing each component to sixteen bits as the reference decoder's storage does.
  /// </summary>
  internal Vp8MotionVector(int row, int column)
    => this._packed = ((short)row << 16) | (ushort)(short)column;

  internal static Vp8MotionVector Zero => default;

  internal int Row => this._packed >> 16;

  internal int Column => (short)this._packed;

  internal bool IsZero => this._packed == 0;

  internal Vp8MotionVector Negated() => new(-this.Row, -this.Column);

  internal Vp8MotionVector Plus(Vp8MotionVector other) => new(this.Row + other.Row, this.Column + other.Column);

  /// <summary>Drops the fractional part, for the version of the format that codes whole pixels only.</summary>
  internal Vp8MotionVector Truncated() => new(this.Row & ~7, this.Column & ~7);

  /// <summary>Holds the vector inside the bounds a macroblock's position allows (RFC 6386, 16.3).</summary>
  internal Vp8MotionVector Clamped(int toLeft, int toRight, int toTop, int toBottom) {
    var column = this.Column;
    var row = this.Row;

    if (column < toLeft)
      column = toLeft;
    else if (column > toRight)
      column = toRight;

    if (row < toTop)
      row = toTop;
    else if (row > toBottom)
      row = toBottom;

    return new(row, column);
  }

  public bool Equals(Vp8MotionVector other) => this._packed == other._packed;

  public override bool Equals(object? obj) => obj is Vp8MotionVector other && this.Equals(other);

  public override int GetHashCode() => this._packed;

  public static bool operator ==(Vp8MotionVector left, Vp8MotionVector right) => left._packed == right._packed;

  public static bool operator !=(Vp8MotionVector left, Vp8MotionVector right) => left._packed != right._packed;
}
