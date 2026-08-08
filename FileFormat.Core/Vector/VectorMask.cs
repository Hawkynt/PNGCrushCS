using System;

namespace FileFormat.Core.Vector;

/// <summary>How much of each pixel a fill is allowed to reach: a clipping path, rasterised.</summary>
/// <remarks>
/// A clip is not something drawn; it is a shape everything after it is confined to. Holding it as
/// coverage rather than as a shape means a later fill only has to multiply by it, and means a clip
/// whose edge runs diagonally softens the shapes inside it the same way it would soften itself.
/// </remarks>
public sealed class VectorMask {

  /// <summary>How wide the mask is, in pixels.</summary>
  public int Width { get; }

  /// <summary>How tall the mask is, in pixels.</summary>
  public int Height { get; }

  /// <summary>One byte a pixel, row by row: nothing gets through at zero and everything at 255.</summary>
  public byte[] Coverage { get; }

  /// <summary>Builds an empty mask, which lets nothing through until something is drawn into it.</summary>
  public VectorMask(int width, int height) {
    if (width < 1 || height < 1)
      throw new ArgumentOutOfRangeException(nameof(width), $"A mask of {width}x{height} has no pixels.");

    this.Width = width;
    this.Height = height;
    this.Coverage = new byte[width * height];
  }

  /// <summary>The two masks together, which is what nesting one clip inside another comes to.</summary>
  public VectorMask IntersectedWith(VectorMask other) {
    ArgumentNullException.ThrowIfNull(other);
    if (other.Width != this.Width || other.Height != this.Height)
      throw new ArgumentOutOfRangeException(nameof(other), "Masks of different sizes cannot be combined.");

    var combined = new VectorMask(this.Width, this.Height);
    for (var i = 0; i < this.Coverage.Length; ++i)
      combined.Coverage[i] = (byte)(this.Coverage[i] * other.Coverage[i] / 255);

    return combined;
  }
}
