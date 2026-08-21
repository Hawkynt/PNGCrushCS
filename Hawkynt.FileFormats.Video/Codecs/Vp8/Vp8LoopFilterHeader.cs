using System;

namespace FileFormat.Codecs.Vp8;

/// <summary>
/// What the frame header says about loop filtering: which filter, how hard, and the per-macroblock
/// adjustments (RFC 6386, 9.4).
/// </summary>
/// <remarks>
/// The deltas here are the counterpart to the ones in <see cref="Vp8Segmentation"/> and behave the
/// other way round: a delta whose update flag is clear keeps the value a previous frame gave it. So
/// a stream can set them once on the frame after a key frame and never mention them again, and a
/// decoder that reset them to zero would filter every later frame slightly too hard or too softly —
/// a difference of a level or two, everywhere, growing across the group of pictures.
/// </remarks>
internal sealed class Vp8LoopFilterHeader {

  internal bool Simple;
  internal int Level;
  internal int Sharpness;
  internal bool DeltasEnabled;

  /// <summary>An adjustment per reference frame, indexed by <see cref="Vp8Reference"/>.</summary>
  internal readonly int[] ReferenceDelta = new int[Vp8Reference.COUNT];

  /// <summary>
  /// An adjustment per coding mode: subblock intra, zero motion, any other motion, split motion.
  /// </summary>
  internal readonly int[] ModeDelta = new int[4];

  internal void Reset() {
    this.Simple = false;
    this.Level = 0;
    this.Sharpness = 0;
    this.DeltasEnabled = false;
    Array.Clear(this.ReferenceDelta);
    Array.Clear(this.ModeDelta);
  }

  internal void Parse(ref Vp8BoolDecoder reader) {
    this.Simple = reader.ReadFlag() != 0;
    this.Level = reader.ReadLiteral(6);
    this.Sharpness = reader.ReadLiteral(3);
    this.DeltasEnabled = reader.ReadFlag() != 0;

    if (!this.DeltasEnabled || reader.ReadFlag() == 0)
      return;

    for (var i = 0; i < this.ReferenceDelta.Length; ++i)
      if (reader.ReadFlag() != 0)
        this.ReferenceDelta[i] = reader.ReadSignedValue(6);

    for (var i = 0; i < this.ModeDelta.Length; ++i)
      if (reader.ReadFlag() != 0)
        this.ModeDelta[i] = reader.ReadSignedValue(6);
  }
}
