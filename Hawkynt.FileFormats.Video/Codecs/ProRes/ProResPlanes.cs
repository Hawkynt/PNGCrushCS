using System;

namespace FileFormat.Codecs.ProRes;

/// <summary>
/// The reconstructed component samples of one frame, at the depth the profile is coded for.
/// </summary>
/// <remarks>
/// Planes rather than packed pixels, and at the coded depth rather than at eight bits, because that
/// is what the decoding process of RDD 36:2022, 7, produces and it is what a comparison against
/// another decoder has to be made on. Packing and narrowing are display steps that happen once, at
/// the end, in <see cref="ProResColorConversion"/>.
/// <para/>
/// The planes are as wide as the <i>encoded</i> picture — a whole number of macroblocks — and as
/// tall as the frame. RDD 36:2022, 7.5.3 has decoders discard the excess pixels at the right and
/// the excess rows at the bottom, and both are discarded here at the point the frame is packed, so
/// that a macroblock straddling the edge is still reconstructed whole and nothing has to special-case
/// the last slice of a row.
/// </remarks>
internal sealed class ProResPlanes {

  internal required int Width { get; init; }
  internal required int Height { get; init; }
  internal required int ChromaWidth { get; init; }
  internal required int ChromaHeight { get; init; }

  /// <summary>The number of bits each sample of <see cref="Luma"/>, <see cref="Cb"/> and
  /// <see cref="Cr"/> occupies.</summary>
  internal required int BitDepth { get; init; }

  internal required ushort[] Luma { get; init; }
  internal required ushort[] Cb { get; init; }
  internal required ushort[] Cr { get; init; }

  /// <summary>The decoded alpha values, at their own depth, or <c>null</c> when the frame has none.</summary>
  internal ushort[]? Alpha { get; init; }

  /// <summary>The depth <see cref="Alpha"/> was coded at — 8 or 16 — or zero when there is none.</summary>
  internal int AlphaBitDepth { get; init; }

  internal static ProResPlanes Allocate(int width, int height, int chromaShift, int bitDepth, int alphaChannelType) {
    var chromaWidth = width >> chromaShift;

    return new() {
      Width = width,
      Height = height,
      ChromaWidth = chromaWidth,
      ChromaHeight = height,
      BitDepth = bitDepth,
      Luma = new ushort[width * height],
      Cb = new ushort[chromaWidth * height],
      Cr = new ushort[chromaWidth * height],

      // RDD 36:2022, Table 7: 1 is 8-bit alpha and 2 is 16-bit. The alpha plane is the size of the
      // luma one whichever it is, because alpha is coded per pixel and never subsampled.
      Alpha = alphaChannelType == 0 ? null : new ushort[width * height],
      AlphaBitDepth = alphaChannelType switch { 1 => 8, 2 => 16, _ => 0 },
    };
  }

  /// <summary>The plane a component index names: 0 luma, 1 blue chroma, 2 red chroma.</summary>
  internal ushort[] Plane(int component) => component switch {
    0 => this.Luma,
    1 => this.Cb,
    _ => this.Cr,
  };

  /// <summary>The width in samples of the plane a component index names.</summary>
  internal int PlaneWidth(int component) => component == 0 ? this.Width : this.ChromaWidth;

  /// <summary>The largest value a sample of a colour component can take at this depth.</summary>
  internal int MaximumSample => (1 << this.BitDepth) - 1;

  internal Span<ushort> Row(int component, int y) {
    var width = this.PlaneWidth(component);
    return this.Plane(component).AsSpan(y * width, width);
  }
}
