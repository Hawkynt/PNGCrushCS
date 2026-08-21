using System;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9;

/// <summary>
/// The compressed header: the transform mode and every change a frame wants made to the probability
/// tables it inherited (specification 6.3).
/// </summary>
/// <remarks>
/// The changes are themselves arithmetic coded, and coded as differences rather than as values, so
/// that leaving a probability alone costs one bool at 252/256 and nudging one costs a handful of bits.
/// A frame that wants none of them still pays for the flags, which is why the smallest possible
/// compressed header is a few dozen bytes rather than nothing.
/// <para/>
/// The order of the reads is the whole of the format here. Every one of these tables is read whether
/// or not the frame turns out to use it, and reading one too few or one too many leaves the arithmetic
/// decoder pointing at the wrong bit for the rest of the header — which does not fail, it just
/// produces different numbers.
/// </remarks>
internal static class Vp9CompressedHeader {

  /// <summary>The probability that a probability is <em>not</em> updated (specification 6.3.3).</summary>
  private const int UPDATE_PROBABILITY = 252;

  internal static void Parse(ref Vp9BoolDecoder reader, Vp9FrameHeader header, Vp9Probabilities probabilities) {
    _ReadTransformMode(ref reader, header);

    if (header.TransformMode == TX_MODE_SELECT)
      _ReadTransformModeProbabilities(ref reader, probabilities);

    _ReadCoefficientProbabilities(ref reader, header, probabilities);

    for (var i = 0; i < SKIP_CONTEXTS; ++i)
      probabilities.Skip[i] = _DiffUpdateProbability(ref reader, probabilities.Skip[i]);

    if (header.FrameIsIntra) {
      header.ReferenceMode = SINGLE_REFERENCE;
      return;
    }

    for (var i = 0; i < INTER_MODE_CONTEXTS; ++i)
    for (var j = 0; j < INTER_MODES - 1; ++j)
      probabilities.InterMode[i * (INTER_MODES - 1) + j] =
        _DiffUpdateProbability(ref reader, probabilities.InterMode[i * (INTER_MODES - 1) + j]);

    if (header.InterpolationFilter == SWITCHABLE)
      for (var j = 0; j < INTERP_FILTER_CONTEXTS; ++j)
      for (var i = 0; i < SWITCHABLE_FILTERS - 1; ++i)
        probabilities.InterpolationFilter[j * (SWITCHABLE_FILTERS - 1) + i] =
          _DiffUpdateProbability(ref reader, probabilities.InterpolationFilter[j * (SWITCHABLE_FILTERS - 1) + i]);

    for (var i = 0; i < IS_INTER_CONTEXTS; ++i)
      probabilities.IsInter[i] = _DiffUpdateProbability(ref reader, probabilities.IsInter[i]);

    _ReadFrameReferenceMode(ref reader, header);
    _ReadFrameReferenceModeProbabilities(ref reader, header, probabilities);

    for (var i = 0; i < BLOCK_SIZE_GROUPS; ++i)
    for (var j = 0; j < INTRA_MODES - 1; ++j)
      probabilities.YMode[i * (INTRA_MODES - 1) + j] =
        _DiffUpdateProbability(ref reader, probabilities.YMode[i * (INTRA_MODES - 1) + j]);

    for (var i = 0; i < PARTITION_CONTEXTS; ++i)
    for (var j = 0; j < PARTITION_TYPES - 1; ++j)
      probabilities.Partition[i * (PARTITION_TYPES - 1) + j] =
        _DiffUpdateProbability(ref reader, probabilities.Partition[i * (PARTITION_TYPES - 1) + j]);

    _ReadMotionVectorProbabilities(ref reader, header, probabilities);
  }

  // ============================================================================================
  // Transform mode
  // ============================================================================================

  private static void _ReadTransformMode(ref Vp9BoolDecoder reader, Vp9FrameHeader header) {
    if (header.Lossless) {
      // A lossless frame uses the Walsh-Hadamard transform, which exists at one size only.
      header.TransformMode = ONLY_4X4;
      return;
    }

    var mode = reader.ReadLiteral(2);
    if (mode == ALLOW_32X32)
      mode += reader.ReadLiteral(1);

    header.TransformMode = mode;
  }

  private static void _ReadTransformModeProbabilities(ref Vp9BoolDecoder reader, Vp9Probabilities probabilities) {
    for (var maximum = TX_8X8; maximum <= TX_32X32; ++maximum)
    for (var context = 0; context < TX_SIZE_CONTEXTS; ++context)
    for (var node = 0; node < maximum; ++node) {
      // The rows are read one whole transform size at a time, so the loops nest size outermost even
      // though the flattened table would read more naturally the other way round.
      var at = (maximum * TX_SIZE_CONTEXTS + context) * (TX_SIZES - 1) + node;
      probabilities.TransformSize[at] = _DiffUpdateProbability(ref reader, probabilities.TransformSize[at]);
    }
  }

  private static void _ReadCoefficientProbabilities(
    ref Vp9BoolDecoder reader, Vp9FrameHeader header, Vp9Probabilities probabilities) {
    var maximum = Vp9Tables.BiggestTransformSize[header.TransformMode];

    for (var size = TX_4X4; size <= maximum; ++size) {
      if (reader.ReadLiteral(1) == 0)
        continue;

      for (var plane = 0; plane < BLOCK_TYPES; ++plane)
      for (var reference = 0; reference < REF_TYPES; ++reference)
      for (var band = 0; band < COEF_BANDS; ++band) {
        // Band zero is the direct current coefficient alone, and its context can only be nought,
        // one or two: there is no coefficient before it to have been large.
        var contexts = band == 0 ? 3 : PREV_COEF_CONTEXTS;
        for (var context = 0; context < contexts; ++context)
        for (var node = 0; node < UNCONSTRAINED_NODES; ++node) {
          var at = CoefficientContext(size, plane, reference, band, context) * UNCONSTRAINED_NODES + node;
          probabilities.Coefficient[at] = _DiffUpdateProbability(ref reader, probabilities.Coefficient[at]);
        }
      }
    }
  }

  // ============================================================================================
  // Reference mode
  // ============================================================================================

  private static void _ReadFrameReferenceMode(ref Vp9BoolDecoder reader, Vp9FrameHeader header) {
    // Compound prediction averages two references, which is only worth offering when two of them lie
    // on opposite sides of the current frame in time. When all three point the same way the format
    // does not even code the flag.
    var compoundAllowed = false;
    for (var i = 1; i < REFS_PER_FRAME; ++i)
      if (header.ReferenceFrameSignBias[i + 1] != header.ReferenceFrameSignBias[1])
        compoundAllowed = true;

    if (!compoundAllowed) {
      header.ReferenceMode = SINGLE_REFERENCE;
      return;
    }

    if (reader.ReadLiteral(1) == 0) {
      header.ReferenceMode = SINGLE_REFERENCE;
      return;
    }

    header.ReferenceMode = reader.ReadLiteral(1) == 0 ? COMPOUND_REFERENCE : REFERENCE_MODE_SELECT;
    header.SetUpCompoundReferenceMode();
  }

  private static void _ReadFrameReferenceModeProbabilities(
    ref Vp9BoolDecoder reader, Vp9FrameHeader header, Vp9Probabilities probabilities) {
    if (header.ReferenceMode == REFERENCE_MODE_SELECT)
      for (var i = 0; i < COMP_MODE_CONTEXTS; ++i)
        probabilities.CompoundMode[i] = _DiffUpdateProbability(ref reader, probabilities.CompoundMode[i]);

    if (header.ReferenceMode != COMPOUND_REFERENCE)
      for (var i = 0; i < REF_CONTEXTS; ++i) {
        probabilities.SingleReference[i * 2] = _DiffUpdateProbability(ref reader, probabilities.SingleReference[i * 2]);
        probabilities.SingleReference[i * 2 + 1] =
          _DiffUpdateProbability(ref reader, probabilities.SingleReference[i * 2 + 1]);
      }

    if (header.ReferenceMode != SINGLE_REFERENCE)
      for (var i = 0; i < REF_CONTEXTS; ++i)
        probabilities.CompoundReference[i] = _DiffUpdateProbability(ref reader, probabilities.CompoundReference[i]);
  }

  // ============================================================================================
  // Motion vector probabilities
  // ============================================================================================

  private static void _ReadMotionVectorProbabilities(
    ref Vp9BoolDecoder reader, Vp9FrameHeader header, Vp9Probabilities probabilities) {
    for (var j = 0; j < MV_JOINTS - 1; ++j)
      probabilities.MotionVectorJoint[j] = _UpdateMotionVectorProbability(ref reader, probabilities.MotionVectorJoint[j]);

    for (var i = 0; i < 2; ++i) {
      probabilities.MotionVectorSign[i] =
        _UpdateMotionVectorProbability(ref reader, probabilities.MotionVectorSign[i]);

      for (var j = 0; j < MV_CLASSES - 1; ++j)
        probabilities.MotionVectorClass[i * (MV_CLASSES - 1) + j] =
          _UpdateMotionVectorProbability(ref reader, probabilities.MotionVectorClass[i * (MV_CLASSES - 1) + j]);

      probabilities.MotionVectorClass0Bit[i] =
        _UpdateMotionVectorProbability(ref reader, probabilities.MotionVectorClass0Bit[i]);

      for (var j = 0; j < MV_OFFSET_BITS; ++j)
        probabilities.MotionVectorBits[i * MV_OFFSET_BITS + j] =
          _UpdateMotionVectorProbability(ref reader, probabilities.MotionVectorBits[i * MV_OFFSET_BITS + j]);
    }

    for (var i = 0; i < 2; ++i) {
      for (var j = 0; j < CLASS0_SIZE; ++j)
      for (var k = 0; k < MV_FR_SIZE - 1; ++k) {
        var at = (i * CLASS0_SIZE + j) * (MV_FR_SIZE - 1) + k;
        probabilities.MotionVectorClass0Fraction[at] =
          _UpdateMotionVectorProbability(ref reader, probabilities.MotionVectorClass0Fraction[at]);
      }

      for (var k = 0; k < MV_FR_SIZE - 1; ++k)
        probabilities.MotionVectorFraction[i * (MV_FR_SIZE - 1) + k] =
          _UpdateMotionVectorProbability(ref reader, probabilities.MotionVectorFraction[i * (MV_FR_SIZE - 1) + k]);
    }

    if (!header.AllowHighPrecisionMotionVectors)
      return;

    for (var i = 0; i < 2; ++i) {
      probabilities.MotionVectorClass0HighPrecision[i] =
        _UpdateMotionVectorProbability(ref reader, probabilities.MotionVectorClass0HighPrecision[i]);
      probabilities.MotionVectorHighPrecision[i] =
        _UpdateMotionVectorProbability(ref reader, probabilities.MotionVectorHighPrecision[i]);
    }
  }

  // ============================================================================================
  // The update coding itself
  // ============================================================================================

  /// <summary>
  /// Reads one probability update, which is a flag and then a difference (specification 6.3.3).
  /// </summary>
  private static byte _DiffUpdateProbability(ref Vp9BoolDecoder reader, byte probability)
    => reader.ReadBool(UPDATE_PROBABILITY) == 0
      ? probability
      : _InverseRemapProbability(_DecodeTerminatedSubexponential(ref reader), probability);

  /// <summary>
  /// Reads the difference itself, in a code that spends fewer bits the smaller it is
  /// (specification 6.3.4).
  /// </summary>
  /// <remarks>
  /// Four ranges, each introduced by a flag: values under 16, under 32, under 64, and the rest. The
  /// last range is not a plain literal either — seven bits cover the values up to 128 and an eighth
  /// is read only above that, which is a whole bit saved on the commonest of the large adjustments.
  /// </remarks>
  private static int _DecodeTerminatedSubexponential(ref Vp9BoolDecoder reader) {
    if (reader.ReadLiteral(1) == 0)
      return reader.ReadLiteral(4);

    if (reader.ReadLiteral(1) == 0)
      return reader.ReadLiteral(4) + 16;

    if (reader.ReadLiteral(1) == 0)
      return reader.ReadLiteral(5) + 32;

    var value = reader.ReadLiteral(7);
    return value < 65 ? value + 64 : (value << 1) - 1 + reader.ReadLiteral(1);
  }

  /// <summary>
  /// Turns a coded difference into the new probability (specification 6.3.5).
  /// </summary>
  /// <remarks>
  /// Two steps that both exist to make small changes cheap. The permutation puts the differences that
  /// change a probability by a lot at the low, cheap end of the code; the recentring then reads the
  /// result as a distance from the probability already held, alternating either side of it, so that a
  /// probability that hardly moves is written in a couple of bits whatever its value is.
  /// </remarks>
  private static byte _InverseRemapProbability(int delta, byte probability) {
    var value = Vp9Tables.InverseMapTable[delta];
    var m = probability - 1;

    return (byte)(m << 1 <= 255
      ? 1 + _InverseRecentreNonNegative(value, m)
      : 255 - _InverseRecentreNonNegative(value, 255 - 1 - m));
  }

  private static int _InverseRecentreNonNegative(int value, int middle) {
    if (value > 2 * middle)
      return value;

    return (value & 1) != 0 ? middle - ((value + 1) >> 1) : middle + (value >> 1);
  }

  /// <summary>
  /// Reads one motion vector probability update, which unlike every other one is a plain seven-bit
  /// value rather than a difference (specification 6.3.17).
  /// </summary>
  /// <remarks>
  /// The low bit is forced to one, which is why the value is written in seven bits rather than eight:
  /// a motion vector probability of zero would make one branch of its tree unreachable, and the format
  /// spends the bit it saves on never having to say so.
  /// </remarks>
  private static byte _UpdateMotionVectorProbability(ref Vp9BoolDecoder reader, byte probability)
    => reader.ReadBool(UPDATE_PROBABILITY) == 0 ? probability : (byte)((reader.ReadLiteral(7) << 1) | 1);
}
