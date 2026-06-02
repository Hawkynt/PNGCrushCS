using System;

namespace FileFormat.Gif;

/// <summary>A (Width, Height) pair. Matches the type the external <c>Hawkynt.GifFileFormat</c> library
/// exposed so consumers can migrate by namespace swap.</summary>
public readonly record struct Dimensions(ushort Width, ushort Height) {
  public Dimensions(int width, int height) : this((ushort)width, (ushort)height) {
    ArgumentOutOfRangeException.ThrowIfNegative(width);
    ArgumentOutOfRangeException.ThrowIfNegative(height);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(width, ushort.MaxValue);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(height, ushort.MaxValue);
  }

  public static Dimensions Empty { get; } = new((ushort)0, (ushort)0);
}
