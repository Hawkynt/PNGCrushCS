using System;

namespace FileFormat.Codecs.Vp8;

/// <summary>
/// The probabilities that survive from one frame to the next: coefficient tokens, motion vector
/// components, and the two intra mode trees used in interframes.
/// </summary>
/// <remarks>
/// These are the state that makes a VP8 interframe undecodable on its own. A frame header may
/// restate any of them, and what it does not restate keeps the value the frame before gave it, all
/// the way back to the last key frame — which resets every one of them to a default.
/// <para/>
/// A frame may also decode with new probabilities and decline to keep them, by clearing
/// <c>refresh_entropy_probs</c>. That is what <see cref="Save"/> and <see cref="Restore"/> are for:
/// the state is copied aside before the updates are read and put back after the frame is decoded, so
/// the next frame starts where this one did. It is how a stream carries a frame that may be dropped
/// without the frames after it going wrong.
/// </remarks>
internal sealed class Vp8Entropy {

  internal const int COEFFICIENT_PROBABILITY_COUNT = 4 * 8 * 3 * (Vp8Trees.TOKEN_COUNT - 1);

  internal readonly byte[] CoefficientProbabilities = new byte[COEFFICIENT_PROBABILITY_COUNT];
  internal readonly byte[] MotionVectorProbabilities = new byte[2 * Vp8Trees.MV_PROBABILITY_COUNT];
  internal readonly byte[] LumaModeProbabilities = new byte[4];
  internal readonly byte[] ChromaModeProbabilities = new byte[3];

  private readonly byte[] _savedCoefficients = new byte[COEFFICIENT_PROBABILITY_COUNT];
  private readonly byte[] _savedMotionVectors = new byte[2 * Vp8Trees.MV_PROBABILITY_COUNT];
  private readonly byte[] _savedLumaModes = new byte[4];
  private readonly byte[] _savedChromaModes = new byte[3];
  private bool _saved;

  internal Vp8Entropy() => this.Reset();

  /// <summary>Puts every probability back to the value a key frame starts from.</summary>
  internal void Reset() {
    Vp8Tables.DefaultCoefficientProbabilities.CopyTo(this.CoefficientProbabilities, 0);
    Vp8Trees.DefaultMotionVectorProbabilities.CopyTo(this.MotionVectorProbabilities);
    Vp8Trees.DefaultLumaModeProbabilities.CopyTo(this.LumaModeProbabilities);
    Vp8Trees.DefaultChromaModeProbabilities.CopyTo(this.ChromaModeProbabilities);
  }

  internal void Save() {
    this.CoefficientProbabilities.CopyTo(this._savedCoefficients, 0);
    this.MotionVectorProbabilities.CopyTo(this._savedMotionVectors, 0);
    this.LumaModeProbabilities.CopyTo(this._savedLumaModes, 0);
    this.ChromaModeProbabilities.CopyTo(this._savedChromaModes, 0);
    this._saved = true;
  }

  internal void Restore() {
    if (!this._saved)
      return;

    this._savedCoefficients.CopyTo(this.CoefficientProbabilities, 0);
    this._savedMotionVectors.CopyTo(this.MotionVectorProbabilities, 0);
    this._savedLumaModes.CopyTo(this.LumaModeProbabilities, 0);
    this._savedChromaModes.CopyTo(this.ChromaModeProbabilities, 0);
    this._saved = false;
  }

  /// <summary>
  /// Reads the probability updates at the end of a frame header (RFC 6386, 9.9, 9.10 and 17.2).
  /// </summary>
  /// <param name="reader">The first partition, positioned after the reference frame fields.</param>
  /// <param name="isKeyFrame">Whether the fields that only interframes carry are present.</param>
  /// <param name="skipEnabled">Set to whether macroblocks may declare themselves free of coefficients.</param>
  /// <param name="skipProbability">Set to the probability that flag is false, when it is present.</param>
  /// <param name="intraProbability">Set to the chance a macroblock is intra-coded; unused in a key frame.</param>
  /// <param name="lastProbability">Set to the chance an inter macroblock predicts from the previous frame.</param>
  /// <param name="goldenProbability">Set to the chance it predicts from the golden rather than the altref frame.</param>
  internal void ParseUpdates(
    ref Vp8BoolDecoder reader,
    bool isKeyFrame,
    out bool skipEnabled,
    out int skipProbability,
    out int intraProbability,
    out int lastProbability,
    out int goldenProbability) {
    var probabilities = this.CoefficientProbabilities;
    var updateProbabilities = Vp8Tables.CoefficientUpdateProbabilities;
    for (var i = 0; i < COEFFICIENT_PROBABILITY_COUNT; ++i)
      if (reader.ReadBool(updateProbabilities[i]) != 0)
        probabilities[i] = (byte)reader.ReadLiteral(8);

    skipEnabled = reader.ReadFlag() != 0;
    skipProbability = skipEnabled ? reader.ReadLiteral(8) : 0;

    intraProbability = 0;
    lastProbability = 0;
    goldenProbability = 0;
    if (isKeyFrame)
      return;

    intraProbability = reader.ReadLiteral(8);
    lastProbability = reader.ReadLiteral(8);
    goldenProbability = reader.ReadLiteral(8);

    if (reader.ReadFlag() != 0)
      for (var i = 0; i < this.LumaModeProbabilities.Length; ++i)
        this.LumaModeProbabilities[i] = (byte)reader.ReadLiteral(8);

    if (reader.ReadFlag() != 0)
      for (var i = 0; i < this.ChromaModeProbabilities.Length; ++i)
        this.ChromaModeProbabilities[i] = (byte)reader.ReadLiteral(8);

    // A motion vector probability is written as seven bits and used as eight: the value is doubled,
    // and a written zero means one rather than zero. A probability of zero would make one branch of
    // the tree unreachable and the other free, which is not a thing the encoder can mean.
    var motionVectorUpdates = Vp8Trees.MotionVectorUpdateProbabilities;
    for (var i = 0; i < this.MotionVectorProbabilities.Length; ++i) {
      if (reader.ReadBool(motionVectorUpdates[i]) == 0)
        continue;

      var written = reader.ReadLiteral(7);
      this.MotionVectorProbabilities[i] = (byte)(written != 0 ? written << 1 : 1);
    }
  }
}
