using System;

namespace FileFormat.Gif;

/// <summary>An (X, Y) frame-position pair. Matches the type the external <c>Hawkynt.GifFileFormat</c>
/// library exposed so consumers can migrate by namespace swap.</summary>
public readonly record struct Offset(ushort X, ushort Y) {
  public Offset(int x, int y) : this((ushort)x, (ushort)y) {
    ArgumentOutOfRangeException.ThrowIfNegative(x);
    ArgumentOutOfRangeException.ThrowIfNegative(y);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(x, ushort.MaxValue);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(y, ushort.MaxValue);
  }

  public static Offset None { get; } = new((ushort)0, (ushort)0);
}
