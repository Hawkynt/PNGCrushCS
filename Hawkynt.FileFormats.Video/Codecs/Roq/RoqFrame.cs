using System;

namespace FileFormat.Codecs.Roq;

/// <summary>
/// One reconstructed RoQ picture, as full-resolution Y, Cb, Cr and alpha sample planes.
/// </summary>
/// <remarks>
/// A normal RoQ codebook cell states one Cb and one Cr value for a 2x2 area of luma, which reads as
/// 4:2:0 only at the instant the cell is painted. Motion compensation moves every reconstructed sample
/// at full pixel precision, so chroma is kept at the picture's own dimensions. The older Trilobyte
/// variant can additionally state four alpha samples per 2x2 codebook cell; ordinary RoQ frames keep
/// this plane fully opaque.
/// </remarks>
internal sealed class RoqFrame {

  internal RoqFrame(int width, int height, bool hasAlpha = false) {
    this.Width = width;
    this.Height = height;
    this.HasAlpha = hasAlpha;
    var samples = checked(width * height);
    this.Y = new byte[samples];
    this.Cb = new byte[samples];
    this.Cr = new byte[samples];
    this.A = new byte[samples];
    if (!hasAlpha)
      Array.Fill(this.A, byte.MaxValue);
  }

  internal int Width { get; }

  internal int Height { get; }

  internal bool HasAlpha { get; }

  internal byte[] Y { get; }

  internal byte[] Cb { get; }

  internal byte[] Cr { get; }

  internal byte[] A { get; }

  /// <summary>Overwrites every sample of this frame with another's, without allocating.</summary>
  internal void CopyFrom(RoqFrame other) {
    if (other.Width != this.Width || other.Height != this.Height)
      throw new ArgumentException("RoQ frame dimensions differ.", nameof(other));

    Array.Copy(other.Y, this.Y, this.Y.Length);
    Array.Copy(other.Cb, this.Cb, this.Cb.Length);
    Array.Copy(other.Cr, this.Cr, this.Cr.Length);
    Array.Copy(other.A, this.A, this.A.Length);
  }

  internal void MakeOpaque() => Array.Fill(this.A, byte.MaxValue);
}
