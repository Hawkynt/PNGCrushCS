namespace FileFormat.Codecs.Mpeg4;

/// <summary>
/// What a bidirectionally coded picture needs to know about the picture it is predicted backwards
/// from: which of its macroblocks were coded, and with what vectors.
/// </summary>
/// <remarks>
/// The direct prediction mode of ISO/IEC 14496-2 7.6.7 has a macroblock of a bidirectionally coded
/// picture carry no vectors of its own. It takes the vectors of the co-located macroblock of the
/// following anchor and scales them by where in time it sits between the two pictures it is predicted
/// from, which means the anchor's motion has to outlive the anchor's decode.
/// <para/>
/// Four vectors per macroblock rather than one, always. A macroblock that carried one vector is
/// stored as that vector four times, so that direct mode reads the same shape whether the anchor used
/// one vector or four — which is what the standard's derivation does, and it costs a few kilobytes on
/// a standard-definition picture.
/// <para/>
/// An intra macroblock is stored as four zero vectors and nothing else. It needs no flag of its own
/// because it has no vector to scale and zero is what direct mode should take from it, and a flag
/// nothing reads is a thing that can be set wrongly without anything noticing.
/// </remarks>
internal sealed class Mpeg4AnchorMotion {

  internal Mpeg4AnchorMotion(int macroblockCount) {
    this.VectorX = new short[macroblockCount * 4];
    this.VectorY = new short[macroblockCount * 4];
    this.IsNotCoded = new bool[macroblockCount];
  }

  /// <summary>Each macroblock's four horizontal vectors, in half-sample units.</summary>
  /// <remarks>
  /// Half-sample units even for a quarter-sample picture. The direct mode derivation of 7.6.7 is
  /// stated over the anchor's vectors at the resolution the anchor coded them, and a picture and its
  /// anchor may not use the same resolution; keeping one unit here means the scaling is one
  /// arithmetic and not two.
  /// </remarks>
  internal short[] VectorX { get; }

  internal short[] VectorY { get; }

  /// <summary>Whether each macroblock carried nothing at all.</summary>
  internal bool[] IsNotCoded { get; }
}
