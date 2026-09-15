using System;

namespace FileFormat.Codecs.Mve;

/// <summary>One native Interplay true-colour picture: RGB555 words, one per pixel.</summary>
internal sealed class MveRgb555Frame {

  internal MveRgb555Frame(int width, int height) {
    this.Width = width;
    this.Height = height;
    this.Pixels = new ushort[checked(width * height)];
  }

  internal int Width { get; }
  internal int Height { get; }
  internal ushort[] Pixels { get; }

  internal void CopyFrom(MveRgb555Frame other) {
    ArgumentNullException.ThrowIfNull(other);
    if (other.Width != this.Width || other.Height != this.Height)
      throw new ArgumentException("RGB555 MVE frame geometry does not match.", nameof(other));
    Array.Copy(other.Pixels, this.Pixels, this.Pixels.Length);
  }
}
