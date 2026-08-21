using System;

namespace FileFormat.Codecs.Mpeg4;

/// <summary>
/// One reconstructed picture, as the three 4:2:0 sample planes it is coded as, with a border of
/// repeated edge samples around each.
/// </summary>
/// <remarks>
/// The border is what makes an MPEG-4 motion vector able to point outside the picture. ISO/IEC
/// 14496-2 7.6.3 has a reference picture extended by repeating its edge samples outward, and a vector
/// reaching past the edge reads that extension; a decoder without it would have to clamp every sample
/// read, which is the same arithmetic done once per sample instead of once per picture and is easy to
/// get subtly wrong at a half-sample position. Padding the planes once, when the picture is finished,
/// makes every later read an ordinary read.
/// <para/>
/// The planes are also a whole number of macroblocks across and down rather than the size of the
/// picture. A stream of 100 by 60 is coded as seven macroblocks by four, and the twelve columns and
/// four rows that reach past the picture are coded, transmitted and reconstructed like any others —
/// later vectors may point into them. Storing the planes cropped would mean inventing what those
/// vectors point at; the crop belongs at the end, where the picture is handed out.
/// </remarks>
internal sealed class Mpeg4Frame {

  /// <summary>
  /// How far outside the picture a vector may reach, in luminance samples.
  /// </summary>
  /// <remarks>
  /// A vector's range at the widest <c>f_code</c> the Advanced Simple Profile allows reaches a long
  /// way outside a small picture, and unrestricted motion compensation permits it. Sixty-four covers
  /// every vector this decoder accepts once the block's own sixteen samples and the one extra a
  /// half-sample interpolation reads are allowed for, and it costs a few hundred kilobytes on a
  /// standard-definition picture.
  /// </remarks>
  internal const int Border = 64;

  internal Mpeg4Frame(int macroblockWidth, int macroblockHeight) {
    this.LumaWidth = macroblockWidth * 16;
    this.LumaHeight = macroblockHeight * 16;
    this.ChromaWidth = macroblockWidth * 8;
    this.ChromaHeight = macroblockHeight * 8;

    this.LumaStride = this.LumaWidth + 2 * Border;
    this.ChromaStride = this.ChromaWidth + Border;
    this.Luma = new byte[this.LumaStride * (this.LumaHeight + 2 * Border)];
    this.Cb = new byte[this.ChromaStride * (this.ChromaHeight + Border)];
    this.Cr = new byte[this.ChromaStride * (this.ChromaHeight + Border)];
  }

  internal int LumaWidth { get; }

  internal int LumaHeight { get; }

  internal int ChromaWidth { get; }

  internal int ChromaHeight { get; }

  /// <summary>How many samples a row of the luminance plane occupies, borders included.</summary>
  internal int LumaStride { get; }

  /// <summary>How many samples a row of a chrominance plane occupies, borders included.</summary>
  internal int ChromaStride { get; }

  /// <summary>Where sample (0, 0) of the luminance plane sits in <see cref="Luma"/>.</summary>
  internal int LumaOrigin => Border * this.LumaStride + Border;

  /// <summary>Where sample (0, 0) of a chrominance plane sits.</summary>
  internal int ChromaOrigin => (Border / 2) * this.ChromaStride + (Border / 2);

  internal byte[] Luma { get; }

  internal byte[] Cb { get; }

  internal byte[] Cr { get; }

  /// <summary>The plane one of a macroblock's six blocks belongs to, with its stride and origin.</summary>
  /// <remarks>
  /// Blocks nought to three are the four luminance quadrants of the macroblock in reading order,
  /// block four is Cb and block five is Cr — ISO/IEC 14496-2, Figure 6-5.
  /// </remarks>
  internal (byte[] Plane, int Stride, int Origin, int Width, int Height) PlaneOf(int blockIndex) => blockIndex switch {
    < 4 => (this.Luma, this.LumaStride, this.LumaOrigin, this.LumaWidth, this.LumaHeight),
    4 => (this.Cb, this.ChromaStride, this.ChromaOrigin, this.ChromaWidth, this.ChromaHeight),
    _ => (this.Cr, this.ChromaStride, this.ChromaOrigin, this.ChromaWidth, this.ChromaHeight),
  };

  /// <summary>
  /// Fills the border of every plane with the edge samples, so that a vector reaching outside reads a
  /// repetition of the edge (ISO/IEC 14496-2, 7.6.3).
  /// </summary>
  /// <remarks>
  /// Called once, when the picture is finished and before anything predicts from it. Doing it per
  /// block instead would be the same arithmetic many more times, and doing it lazily would mean a
  /// picture that had been predicted from before it was padded — which is a class of bug that shows
  /// only at the edges of moving objects.
  /// </remarks>
  internal void PadBorders() {
    _Pad(this.Luma, this.LumaStride, this.LumaOrigin, this.LumaWidth, this.LumaHeight, Border);
    _Pad(this.Cb, this.ChromaStride, this.ChromaOrigin, this.ChromaWidth, this.ChromaHeight, Border / 2);
    _Pad(this.Cr, this.ChromaStride, this.ChromaOrigin, this.ChromaWidth, this.ChromaHeight, Border / 2);
  }

  private static void _Pad(byte[] plane, int stride, int origin, int width, int height, int border) {
    // Sideways first, so that the rows copied above and below already carry their own side borders
    // and the corners come out as the corner sample repeated rather than as whatever was there.
    for (var y = 0; y < height; ++y) {
      var row = origin + y * stride;
      var left = plane[row];
      var right = plane[row + width - 1];
      for (var x = 1; x <= border; ++x) {
        plane[row - x] = left;
        plane[row + width - 1 + x] = right;
      }
    }

    var top = origin - border;
    var bottom = origin + (height - 1) * stride - border;
    var span = width + 2 * border;
    for (var y = 1; y <= border; ++y) {
      Array.Copy(plane, top, plane, top - y * stride, span);
      Array.Copy(plane, bottom, plane, bottom + y * stride, span);
    }
  }
}
