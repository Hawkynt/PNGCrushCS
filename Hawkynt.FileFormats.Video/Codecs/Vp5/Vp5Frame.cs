using System;

namespace FileFormat.Codecs.Vp5;

/// <summary>Which picture a macroblock predicts from.</summary>
/// <remarks>
/// The numbering is the format's and is used as an array index, so it is not free to change.
/// <see cref="Current"/> is what an intra macroblock names, which is why a direct-current predictor
/// from an intra neighbour cannot be confused with one from a predicted neighbour.
/// </remarks>
internal enum Vp5Reference {
  None = -1,
  Current = 0,
  Previous = 1,
  Golden = 2,
}

/// <summary>The ten macroblock coding modes.</summary>
/// <remarks>The values are the format's own and index the type trees and statistics tables.</remarks>
internal enum Vp5MacroblockType {
  InterNoVectorPrevious = 0,
  Intra = 1,
  InterDeltaPrevious = 2,
  InterVector1Previous = 3,
  InterVector2Previous = 4,
  InterNoVectorGolden = 5,
  InterDeltaGolden = 6,
  InterFourVectors = 7,
  InterVector1Golden = 8,
  InterVector2Golden = 9,
}

/// <summary>A motion vector, in half-samples of luminance.</summary>
internal readonly record struct Vp5MotionVector(short X, short Y) {
  internal static readonly Vp5MotionVector Zero = new(0, 0);
}

/// <summary>One macroblock's decoded mode and the vector its neighbours may predict from.</summary>
internal struct Vp5Macroblock {
  internal Vp5MacroblockType Type;
  internal Vp5MotionVector MotionVector;
}

/// <summary>What one block contributes to its neighbours' direct-current prediction.</summary>
internal struct Vp5DcPredictor {
  internal Vp5Reference Reference;
  internal short Dc;
  internal byte NotNullDc;
}

/// <summary>
/// One reconstructed 4:2:0 picture, in coded rather than display row order.
/// </summary>
/// <remarks>
/// VP5 codes its rows bottom upwards — the first macroblock row of the bitstream is the bottom one of
/// the picture. Reconstruction stays in that order throughout, including for prediction, because
/// motion vectors are in it too; the picture is turned the right way up once, on the way out, by
/// <see cref="TopDown"/>. Doing it the other way round would mean flipping every reference read.
/// <para/>
/// The planes are whole macroblocks, so their dimensions are always multiples of sixteen and eight.
/// VP5 has no cropping of its own: a key frame states a display size in macroblocks as well as a
/// coded one, but both are macroblock counts, and the display size only ever names a subrectangle of
/// whole macroblocks.
/// </remarks>
internal sealed class Vp5Frame {

  internal Vp5Frame(int macroblockWidth, int macroblockHeight) {
    this.LumaWidth = macroblockWidth * 16;
    this.LumaHeight = macroblockHeight * 16;
    this.ChromaWidth = macroblockWidth * 8;
    this.ChromaHeight = macroblockHeight * 8;
    this.Luma = new byte[this.LumaWidth * this.LumaHeight];
    this.Cb = new byte[this.ChromaWidth * this.ChromaHeight];
    this.Cr = new byte[this.ChromaWidth * this.ChromaHeight];
  }

  internal int LumaWidth { get; }
  internal int LumaHeight { get; }
  internal int ChromaWidth { get; }
  internal int ChromaHeight { get; }
  internal byte[] Luma { get; }
  internal byte[] Cb { get; }
  internal byte[] Cr { get; }

  internal byte[] Plane(int plane) => plane switch {
    0 => this.Luma,
    1 => this.Cb,
    2 => this.Cr,
    _ => throw new ArgumentOutOfRangeException(nameof(plane)),
  };

  internal int PlaneWidth(int plane) => plane == 0 ? this.LumaWidth : this.ChromaWidth;
  internal int PlaneHeight(int plane) => plane == 0 ? this.LumaHeight : this.ChromaHeight;

  internal void CopyFrom(Vp5Frame source) {
    source.Luma.AsSpan().CopyTo(this.Luma);
    source.Cb.AsSpan().CopyTo(this.Cb);
    source.Cr.AsSpan().CopyTo(this.Cr);
  }

  /// <summary>One plane the right way up, as a display would hold it.</summary>
  internal byte[] TopDown(int plane) {
    var source = this.Plane(plane);
    var width = this.PlaneWidth(plane);
    var height = this.PlaneHeight(plane);
    var result = new byte[source.Length];

    for (var row = 0; row < height; ++row)
      source.AsSpan((height - 1 - row) * width, width).CopyTo(result.AsSpan(row * width, width));

    return result;
  }
}
