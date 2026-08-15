using System;

namespace FileFormat.Core;

/// <summary>An exact ratio of two integers, used for time bases and frame rates.</summary>
/// <remarks>
/// Timing is kept as a ratio rather than a <see cref="double"/> because the rates that matter are
/// not representable as one. NTSC video runs at 30000/1001 frames a second; stored as 29.97 it
/// drifts by about one frame every thousand, which over a feature-length film is seconds of
/// desynchronisation between picture and sound. A container states the ratio and so does this.
/// </remarks>
/// <param name="Numerator">The numerator, which may be zero to mean "unstated".</param>
/// <param name="Denominator">The denominator, which is one for whole numbers and never zero in a
/// ratio that says anything.</param>
public readonly record struct Rational(long Numerator, long Denominator) {

  /// <summary>The ratio a container states when it does not know the value.</summary>
  public static readonly Rational Unknown = new(0, 1);

  /// <summary>Whether this ratio carries a value at all.</summary>
  public bool IsKnown => this.Numerator != 0 && this.Denominator != 0;

  /// <summary>The ratio as a floating-point number, for display and for comparisons that tolerate rounding.</summary>
  public double ToDouble() => this.Denominator == 0 ? 0d : (double)this.Numerator / this.Denominator;

  /// <summary>Multiplies a count of units by this ratio and returns the result as a time span.</summary>
  /// <remarks>
  /// Used to turn a timestamp in a stream's own ticks into wall-clock time: the stream states the
  /// tick as a ratio of seconds, and the timestamp counts them.
  /// </remarks>
  public TimeSpan Scale(long units) {
    if (!this.IsKnown)
      return TimeSpan.Zero;

    // Through Int128 rather than double so a long timestamp against a microsecond time base keeps
    // every tick: 2^63 microseconds is beyond what a double holds without rounding, and a rounded
    // timestamp is a frame shown at the wrong moment.
    var ticks = (Int128)units * this.Numerator * TimeSpan.TicksPerSecond / this.Denominator;
    return TimeSpan.FromTicks((long)ticks);
  }

  public override string ToString() => this.IsKnown ? $"{this.Numerator}/{this.Denominator}" : "unknown";
}
