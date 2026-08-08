using System;
using System.Collections.Generic;

namespace FileFormat.Core.Vector;

/// <summary>What colour a fill puts at a given place on the surface.</summary>
/// <remarks>
/// Most fills are one colour everywhere and say so with <see cref="Solid"/>. The rest are the
/// gradients a drawing format can name instead of a colour, and those are a function of position
/// rather than a constant — which is the whole of the difference, so it is the whole of what this
/// abstracts.
/// </remarks>
public abstract class VectorPaint {

  /// <summary>The colour at a point on the surface, in pixels.</summary>
  public abstract Rgba32 At(double x, double y);

  /// <summary>Whether the paint is the same colour everywhere, which lets the filler skip the lookup.</summary>
  public virtual bool IsUniform => false;

  /// <summary>One colour everywhere.</summary>
  public static VectorPaint Solid(Rgba32 colour) => new SolidPaint(colour);

  private sealed class SolidPaint(Rgba32 colour) : VectorPaint {
    public override Rgba32 At(double x, double y) => colour;
    public override bool IsUniform => true;
  }
}

/// <summary>One colour of a gradient and how far along it sits.</summary>
public readonly record struct GradientStop(double Offset, Rgba32 Colour);

/// <summary>What a gradient does outside the run from its first stop to its last.</summary>
public enum GradientSpread {

  /// <summary>The end colours carry on unchanged.</summary>
  Pad,

  /// <summary>The run repeats, every other one mirrored.</summary>
  Reflect,

  /// <summary>The run repeats from the beginning each time.</summary>
  Repeat
}

/// <summary>A colour ramp laid along a line or out from a centre.</summary>
/// <remarks>
/// Both kinds come to the same thing once the position is turned into a number from zero to one:
/// how far along the ramp this point is. The line case projects the point onto the vector between
/// the two ends; the round case measures the distance from the focus, scaled by the radius. What
/// happens outside nought to one is the spread rule.
/// </remarks>
public sealed class GradientPaint : VectorPaint {

  private readonly GradientStop[] _stops;
  private readonly bool _radial;
  private readonly Matrix2D _inverse;
  private readonly double _x0, _y0, _x1, _y1, _radius;
  private readonly GradientSpread _spread;

  private GradientPaint(IEnumerable<GradientStop> stops, bool radial, double x0, double y0, double x1, double y1, double radius, Matrix2D toPaintSpace, GradientSpread spread) {
    var ordered = new List<GradientStop>(stops);
    ordered.Sort(static (a, b) => a.Offset.CompareTo(b.Offset));
    if (ordered.Count == 0)
      throw new ArgumentOutOfRangeException(nameof(stops), "A gradient needs at least one stop.");

    this._stops = ordered.ToArray();
    this._radial = radial;
    this._x0 = x0;
    this._y0 = y0;
    this._x1 = x1;
    this._y1 = y1;
    this._radius = radius;
    this._inverse = toPaintSpace;
    this._spread = spread;
  }

  /// <summary>A ramp running from one point to another.</summary>
  /// <param name="toPaintSpace">The transform taking a surface pixel back into the gradient's own frame.</param>
  public static GradientPaint Linear(IEnumerable<GradientStop> stops, double x0, double y0, double x1, double y1, Matrix2D toPaintSpace, GradientSpread spread)
    => new(stops, false, x0, y0, x1, y1, 0, toPaintSpace, spread);

  /// <summary>A ramp running out from a focus to a circle of the given radius about a centre.</summary>
  public static GradientPaint Radial(IEnumerable<GradientStop> stops, double centreX, double centreY, double radius, double focusX, double focusY, Matrix2D toPaintSpace, GradientSpread spread)
    => new(stops, true, centreX, centreY, focusX, focusY, radius, toPaintSpace, spread);

  public override Rgba32 At(double x, double y) {
    var (px, py) = this._inverse.Apply(x, y);
    var t = this._radial ? this._RadialOffset(px, py) : this._LinearOffset(px, py);
    return this._ColourAt(_Spread(t, this._spread));
  }

  private double _LinearOffset(double x, double y) {
    var dx = this._x1 - this._x0;
    var dy = this._y1 - this._y0;
    var lengthSquared = dx * dx + dy * dy;
    return lengthSquared <= 0 ? 0 : ((x - this._x0) * dx + (y - this._y0) * dy) / lengthSquared;
  }

  private double _RadialOffset(double x, double y) {
    if (this._radius <= 0)
      return 1;

    var dx = x - this._x0;
    var dy = y - this._y0;
    return Math.Sqrt(dx * dx + dy * dy) / this._radius;
  }

  private static double _Spread(double t, GradientSpread spread) {
    if (!double.IsFinite(t))
      return 0;

    switch (spread) {
      case GradientSpread.Repeat:
        return t - Math.Floor(t);
      case GradientSpread.Reflect:
        var folded = Math.Abs(t) % 2;
        return folded > 1 ? 2 - folded : folded;
      default:
        return Math.Clamp(t, 0, 1);
    }
  }

  private Rgba32 _ColourAt(double t) {
    if (t <= this._stops[0].Offset)
      return this._stops[0].Colour;

    for (var i = 1; i < this._stops.Length; ++i) {
      var stop = this._stops[i];
      if (t > stop.Offset)
        continue;

      var previous = this._stops[i - 1];
      var span = stop.Offset - previous.Offset;
      var f = span <= 0 ? 0 : (t - previous.Offset) / span;
      return new(
        _Mix(previous.Colour.R, stop.Colour.R, f),
        _Mix(previous.Colour.G, stop.Colour.G, f),
        _Mix(previous.Colour.B, stop.Colour.B, f),
        _Mix(previous.Colour.A, stop.Colour.A, f)
      );
    }

    return this._stops[^1].Colour;
  }

  private static byte _Mix(byte from, byte to, double f) => (byte)Math.Clamp((int)Math.Round(from + (to - from) * f), 0, 255);
}
