using System;

namespace FileFormat.Codecs.HuffYuv;

/// <summary>One decoded plane: its samples and how wide a row of them is.</summary>
internal sealed class HuffYuvPlane {

  internal HuffYuvPlane(int width, int height) {
    this.Width = width;
    this.Height = height;
    this.Samples = new byte[width * height];
  }

  internal int Width { get; }
  internal int Height { get; }
  internal byte[] Samples { get; }

  internal Span<byte> Row(int y) => this.Samples.AsSpan(y * this.Width, this.Width);
  internal ReadOnlySpan<byte> ReadRow(int y) => this.Samples.AsSpan(y * this.Width, this.Width);
}
