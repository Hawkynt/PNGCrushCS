using System;

namespace FileFormat.Core;

/// <summary>
/// An inclusive integer range <c>[Min, Max]</c> with optional <see cref="Step"/> granularity.
/// Used to declare allowed-size constraints such as
/// <see cref="IImageFormatMetadata{TSelf}.AllowedPaletteRanges"/> and
/// <see cref="IImageFormatMetadata{TSelf}.AllowedDimensions"/>.
/// </summary>
/// <remarks>
/// Single-point ranges are typically expressed via the derived <see cref="FixedValue"/>,
/// which has an implicit conversion from <see cref="int"/>. Because <see cref="IntegerRange"/>
/// itself also defines an implicit conversion from <see cref="int"/> (producing a
/// <see cref="FixedValue"/>), arrays can mix literals and explicit ranges naturally:
/// <code>
/// IntegerRange[] discrete = [2, 16, 256];                                 // each int -&gt; FixedValue
/// IntegerRange[] range    = [new IntegerRange(2, 256)];                   // a single range, step 1
/// IntegerRange[] stepped  = [new IntegerRange(8, 4096, step: 8)];         // multiples of 8 in [8..4096]
/// IntegerRange[] mixed    = [new IntegerRange(16, 32), 64, 128];          // range + fixed values
/// </code>
/// </remarks>
public record class IntegerRange(int Min, int Max) {

  /// <summary>Granularity within the range. <c>1</c> = continuous (default). Larger = "multiple of N" constraint.
  /// E.g. <c>Step = 8</c> with <c>[8..4096]</c> allows 8, 16, 24, ..., 4096.</summary>
  public int Step { get; init; } = 1;

  /// <summary>True if this range collapses to a single value (<c>Min == Max</c>).</summary>
  public bool IsFixed => this.Min == this.Max;

  /// <summary>True if <paramref name="value"/> is inside the range AND aligned to <see cref="Step"/>.</summary>
  public bool Contains(int value) =>
    value >= this.Min && value <= this.Max && (value - this.Min) % this.Step == 0;

  /// <summary>Returns the closest valid value to <paramref name="value"/> within the range and step.
  /// Clamps to <c>[Min, Max]</c>, then snaps to the nearest step boundary.</summary>
  public int SnapToValid(int value) {
    if (value <= this.Min) return this.Min;
    if (value >= this.Max) {
      var lastStep = this.Min + ((this.Max - this.Min) / this.Step) * this.Step;
      return lastStep;
    }
    if (this.Step <= 1) return value;
    var rel = value - this.Min;
    var down = this.Min + (rel / this.Step) * this.Step;
    var up = down + this.Step;
    if (up > this.Max) up = down;
    return (value - down) < (up - value) ? down : up;
  }

  /// <summary>Convenience secondary constructor with explicit step.</summary>
  public IntegerRange(int min, int max, int step) : this(min, max) { this.Step = step; }

  /// <summary>Implicit conversion from <see cref="int"/> — produces a single-point <see cref="FixedValue"/>.</summary>
  public static implicit operator IntegerRange(int value) => new FixedValue(value);
}

/// <summary>An <see cref="IntegerRange"/> with <c>Min == Max</c> — a single fixed value.</summary>
public sealed record class FixedValue(int Value) : IntegerRange(Value, Value) {

  /// <summary>Implicit conversion from <see cref="int"/>.</summary>
  public static implicit operator FixedValue(int value) => new(value);
}
