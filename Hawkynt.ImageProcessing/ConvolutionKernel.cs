using System;

namespace Hawkynt.ImageProcessing;

/// <summary>A rectangular grid of weights, centred on its middle cell.</summary>
/// <remarks>
/// Only odd sizes are accepted. An even kernel has no middle cell, so it shifts the picture by half a
/// pixel in whichever direction its author happened to round, and that is a defect in every caller
/// that did not mean it.
/// </remarks>
public sealed class ConvolutionKernel {

  private readonly double[] _weights;

  /// <summary>Creates a kernel from weights listed row by row, top row first.</summary>
  public ConvolutionKernel(int width, int height, ReadOnlySpan<double> weights) {
    if (width < 1 || width % 2 == 0)
      throw new ArgumentOutOfRangeException(nameof(width), width, "A kernel is an odd number of cells across.");
    if (height < 1 || height % 2 == 0)
      throw new ArgumentOutOfRangeException(nameof(height), height, "A kernel is an odd number of cells high.");
    if (weights.Length != width * height)
      throw new ArgumentException($"A {width}x{height} kernel has {width * height} weights, not {weights.Length}.", nameof(weights));

    this.Width = width;
    this.Height = height;
    this._weights = weights.ToArray();
  }

  /// <summary>Cells across.</summary>
  public int Width { get; }

  /// <summary>Cells down.</summary>
  public int Height { get; }

  /// <summary>The weight in one cell, with the origin at the top left.</summary>
  public double this[int x, int y] => this._weights[y * this.Width + x];

  /// <summary>The sum of every weight: one for a kernel that keeps brightness, zero for a detector.</summary>
  public double Sum {
    get {
      var sum = 0.0;
      foreach (var weight in this._weights)
        sum += weight;

      return sum;
    }
  }
}
