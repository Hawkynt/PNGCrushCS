using System;
using System.IO;

namespace FileFormat.Codecs.H265;

/// <summary>
/// One short-term reference picture set — ITU-T H.265, clauses 7.3.7 and 7.4.8.
/// </summary>
/// <remarks>
/// This is the structure HEVC replaced H.264's sliding window and memory management commands with,
/// and it is a better idea. H.264 said what to <em>do</em> to the buffer at each picture, so a
/// decoder entering a stream part way through had to have seen every instruction since the last
/// refresh to know what the buffer held. HEVC instead says, in every picture, which pictures the
/// buffer is supposed to hold right now — as offsets from this picture's own count. Everything not
/// named is dropped. A decoder that joins late is therefore correct at the first picture it reads
/// rather than after an unknown delay, and a picture lost in transit cannot corrupt the buffer's
/// contents for the rest of the sequence, only the pictures that referred to it.
/// <para/>
/// The sets are split into pictures before this one and pictures after it, each ordered by distance,
/// and each entry says whether the current picture may itself refer to it or whether it is only being
/// kept for a later one. Sets are usually few and repeated, so the sequence parameter set carries a
/// list of them and a slice names one by index; a slice may also code its own.
/// <para/>
/// A set may also be coded as a difference from an earlier set, which is what
/// <see cref="_ParsePredicted"/> unpicks. The predicted form is not a shorthand for the same values —
/// it re-derives every offset relative to a shifted origin and re-sorts them, so a set predicted from
/// another can name pictures the other did not.
/// </remarks>
internal sealed class H265ShortTermReferencePictureSet {

  private H265ShortTermReferencePictureSet(int[] deltaPocS0, bool[] usedS0, int[] deltaPocS1, bool[] usedS1) {
    this.DeltaPocS0 = deltaPocS0;
    this.UsedByCurrPicS0 = usedS0;
    this.DeltaPocS1 = deltaPocS1;
    this.UsedByCurrPicS1 = usedS1;
  }

  /// <summary>How far before this picture each earlier reference is, most recent first — negative.</summary>
  internal int[] DeltaPocS0 { get; }

  /// <summary>Whether the current picture may refer to each earlier reference.</summary>
  internal bool[] UsedByCurrPicS0 { get; }

  /// <summary>How far after this picture each later reference is, nearest first — positive.</summary>
  internal int[] DeltaPocS1 { get; }

  /// <summary>Whether the current picture may refer to each later reference.</summary>
  internal bool[] UsedByCurrPicS1 { get; }

  internal int NegativeCount => this.DeltaPocS0.Length;

  internal int PositiveCount => this.DeltaPocS1.Length;

  /// <summary><c>NumDeltaPocs</c>: every picture the set names, whether the current one uses it or not.</summary>
  internal int TotalCount => this.NegativeCount + this.PositiveCount;

  /// <summary>An empty set, which is what an instantaneous refresh picture has.</summary>
  internal static readonly H265ShortTermReferencePictureSet Empty = new([], [], [], []);

  /// <summary>
  /// Reads one set — <c>st_ref_pic_set( stRpsIdx )</c>.
  /// </summary>
  /// <param name="index">
  /// Which set this is. A slice coding its own passes <paramref name="count"/>, which is one past the
  /// last of the sequence parameter set's.
  /// </param>
  /// <param name="count">How many sets the sequence parameter set declares.</param>
  /// <param name="known">The sets read so far, which a predicted set refers back into.</param>
  internal static H265ShortTermReferencePictureSet Parse(
    ref H265BitReader reader, int index, int count, H265ShortTermReferencePictureSet[] known) {
    var predicted = index != 0 && reader.ReadFlag();
    if (!predicted)
      return _ParseExplicit(ref reader);

    // Only a slice's own set may name a reference other than the immediately preceding one, because
    // only it is at an index the sequence parameter set's list does not run up to.
    var back = index == count ? reader.ReadUnsignedExpGolomb() + 1 : 1;
    var referenceIndex = index - back;

    if (referenceIndex < 0 || referenceIndex >= known.Length || known[referenceIndex] == null)
      throw new InvalidDataException(
        $"An H.265 short-term reference picture set at index {index} predicts from set {referenceIndex}, which has "
        + "not been read. delta_idx_minus1 (clause 7.4.8) may only name an earlier set.");

    return _ParsePredicted(ref reader, known[referenceIndex]);
  }

  private static H265ShortTermReferencePictureSet _ParseExplicit(ref H265BitReader reader) {
    var negatives = reader.ReadUnsignedExpGolomb();
    var positives = reader.ReadUnsignedExpGolomb();

    // The bound is the decoded picture buffer's, and a set larger than it cannot be satisfied. It is
    // checked here rather than at use because the numbers size two arrays that a corrupt stream
    // could otherwise make enormous.
    if (negatives > 16 || positives > 16)
      throw new InvalidDataException(
        $"An H.265 short-term reference picture set names {negatives} earlier and {positives} later pictures. No "
        + "level defined by the standard permits a decoded picture buffer larger than sixteen, so these bytes are "
        + "not a reference picture set.");

    var deltaPocS0 = new int[negatives];
    var usedS0 = new bool[negatives];
    var deltaPocS1 = new int[positives];
    var usedS1 = new bool[positives];

    // Each offset is coded as the step from the one before, so the list arrives sorted by increasing
    // distance and never has to be sorted here.
    var previous = 0;
    for (var i = 0; i < negatives; ++i) {
      previous -= reader.ReadUnsignedExpGolomb() + 1;
      deltaPocS0[i] = previous;
      usedS0[i] = reader.ReadFlag();
    }

    previous = 0;
    for (var i = 0; i < positives; ++i) {
      previous += reader.ReadUnsignedExpGolomb() + 1;
      deltaPocS1[i] = previous;
      usedS1[i] = reader.ReadFlag();
    }

    return new(deltaPocS0, usedS0, deltaPocS1, usedS1);
  }

  /// <summary>
  /// Re-derives a set from an earlier one shifted by a stated distance — the second half of clause 7.4.8.
  /// </summary>
  /// <remarks>
  /// One flag pair per picture of the reference set, plus one for the reference picture itself, says
  /// which of them survive into this set. What makes this more than a copy is the shift: an offset
  /// that was three pictures back becomes one picture forward if the origin moved four, and so
  /// changes which of the two halves it belongs to. That is why the earlier half is built by walking
  /// the reference's <em>later</em> half backwards first — after the shift those are the ones nearest
  /// this picture, and the halves have to come out ordered by distance.
  /// </remarks>
  private static H265ShortTermReferencePictureSet _ParsePredicted(
    ref H265BitReader reader, H265ShortTermReferencePictureSet reference) {
    var deltaRps = (1 - 2 * reader.ReadBit()) * (reader.ReadUnsignedExpGolomb() + 1);

    var total = reference.TotalCount;
    var usedByCurr = new bool[total + 1];
    var useDelta = new bool[total + 1];

    for (var j = 0; j <= total; ++j) {
      usedByCurr[j] = reader.ReadFlag();
      useDelta[j] = usedByCurr[j] || reader.ReadFlag();
    }

    var negatives = reference.NegativeCount;
    var positives = reference.PositiveCount;

    var deltaPocS0 = new int[total + 1];
    var usedS0 = new bool[total + 1];
    var count0 = 0;

    for (var j = positives - 1; j >= 0; --j) {
      var delta = reference.DeltaPocS1[j] + deltaRps;
      if (delta >= 0 || !useDelta[negatives + j])
        continue;

      deltaPocS0[count0] = delta;
      usedS0[count0++] = usedByCurr[negatives + j];
    }

    if (deltaRps < 0 && useDelta[total]) {
      deltaPocS0[count0] = deltaRps;
      usedS0[count0++] = usedByCurr[total];
    }

    for (var j = 0; j < negatives; ++j) {
      var delta = reference.DeltaPocS0[j] + deltaRps;
      if (delta >= 0 || !useDelta[j])
        continue;

      deltaPocS0[count0] = delta;
      usedS0[count0++] = usedByCurr[j];
    }

    var deltaPocS1 = new int[total + 1];
    var usedS1 = new bool[total + 1];
    var count1 = 0;

    for (var j = negatives - 1; j >= 0; --j) {
      var delta = reference.DeltaPocS0[j] + deltaRps;
      if (delta <= 0 || !useDelta[j])
        continue;

      deltaPocS1[count1] = delta;
      usedS1[count1++] = usedByCurr[j];
    }

    if (deltaRps > 0 && useDelta[total]) {
      deltaPocS1[count1] = deltaRps;
      usedS1[count1++] = usedByCurr[total];
    }

    for (var j = 0; j < positives; ++j) {
      var delta = reference.DeltaPocS1[j] + deltaRps;
      if (delta <= 0 || !useDelta[negatives + j])
        continue;

      deltaPocS1[count1] = delta;
      usedS1[count1++] = usedByCurr[negatives + j];
    }

    return new(deltaPocS0[..count0], usedS0[..count0], deltaPocS1[..count1], usedS1[..count1]);
  }
}
