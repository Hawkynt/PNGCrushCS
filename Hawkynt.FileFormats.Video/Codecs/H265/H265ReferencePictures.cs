using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.H265;

/// <summary>
/// The decoded picture buffer: which pictures are held, what they are for, and in what order they
/// are shown — ITU-T H.265, clauses 8.3.1 to 8.3.4 and Annex C.
/// </summary>
/// <remarks>
/// HEVC states the buffer's whole contents in every picture rather than telling the decoder what to
/// change, and that is the difference from H.264 worth understanding. A slice header names, as
/// offsets from its own count, exactly which pictures are supposed to be present; anything not named
/// is dropped. So a decoder that joins the stream part way through is in the right state at the first
/// picture it reads rather than after an unknown number of instructions it did not see, and a lost
/// picture damages only the pictures that referred to it.
/// <para/>
/// <b>Display order is not decoding order</b> as soon as bidirectional prediction is used: a picture
/// that others predict forwards and backwards from has to be decoded before both of them and shown
/// between them. The count each picture carries is what puts them back, and how long a picture may
/// wait is stated in the sequence parameter set — so a decoder holds finished pictures until it can
/// prove none still to come belongs before them.
/// </remarks>
internal sealed class H265ReferencePictures {

  private readonly List<H265Picture> _buffer = [];
  private readonly Queue<H265Picture> _output = [];

  private int _previousPocLsb;
  private int _previousPocMsb;
  private bool _sawFirstPicture;

  /// <summary>Whether the last random access point was entered at, which makes its leading pictures undecodable.</summary>
  private bool _skipRandomAccessSkippedLeading;

  internal H265Picture[] ShortTermBefore { get; private set; } = [];

  internal H265Picture[] ShortTermAfter { get; private set; } = [];

  internal H265Picture[] LongTermCurrent { get; private set; } = [];

  /// <summary>Whether a picture of this kind is to be skipped rather than decoded.</summary>
  /// <remarks>
  /// A random access skipped leading picture follows a clean random access point in decoding order,
  /// precedes it in output order, and predicts from pictures before it. When the stream was entered
  /// at that access point those pictures do not exist, so the standard says the picture is not
  /// decoded at all — which is the right answer, and the one that keeps a decoder from inventing a
  /// reference and producing a plausible wrong picture.
  /// </remarks>
  internal bool ShouldSkip(H265NalUnit nal) {
    ArgumentNullException.ThrowIfNull(nal);

    return this._skipRandomAccessSkippedLeading
           && nal.Type is H265NalUnitType.RandomAccessSkippedLeadingNonReference
             or H265NalUnitType.RandomAccessSkippedLeadingReference;
  }

  /// <summary>
  /// The picture order count this picture carries — clause 8.3.1.
  /// </summary>
  /// <remarks>
  /// Only the low bits are transmitted, because the count only has to distinguish the pictures the
  /// buffer holds at once. The high bits are rebuilt by watching for the low bits to wrap, which is
  /// detected by a jump of more than half their range — a real step that large would mean a
  /// reordering distance no level permits.
  /// </remarks>
  internal int ComputePictureOrderCount(H265SliceHeader header) {
    ArgumentNullException.ThrowIfNull(header);

    var nal = header.Nal;
    var noPriorPictures = nal.IsInstantaneousRefresh || nal.IsBrokenLinkAccess || !this._sawFirstPicture;

    if (nal.IsRandomAccessPoint && noPriorPictures)
      return header.PicOrderCntLsb;

    var half = header.Sps.MaxPicOrderCntLsb >> 1;
    var lsb = header.PicOrderCntLsb;

    var msb = this._previousPocMsb;
    if (lsb < this._previousPocLsb && this._previousPocLsb - lsb >= half)
      msb += header.Sps.MaxPicOrderCntLsb;
    else if (lsb > this._previousPocLsb && lsb - this._previousPocLsb > half)
      msb -= header.Sps.MaxPicOrderCntLsb;

    return msb + lsb;
  }

  /// <summary>
  /// Marks the buffer according to what this picture says it should hold — clause 8.3.2.
  /// </summary>
  internal void ApplyReferencePictureSet(H265SliceHeader header, int poc) {
    ArgumentNullException.ThrowIfNull(header);

    var nal = header.Nal;
    var entersHere = nal.IsInstantaneousRefresh || nal.IsBrokenLinkAccess || !this._sawFirstPicture;

    if (nal.IsRandomAccessPoint) {
      this._skipRandomAccessSkippedLeading = entersHere;

      if (entersHere) {
        // Everything the buffer held is unreachable from here. Pictures still waiting to be shown
        // are shown first, which is what an instantaneous refresh means.
        foreach (var picture in this._buffer)
          picture.Marking = H265ReferenceMarking.Unused;

        this._DrainOutput();
        this._buffer.RemoveAll(static picture => picture.WasOutput || !picture.IsOutput);

        this.ShortTermBefore = [];
        this.ShortTermAfter = [];
        this.LongTermCurrent = [];
        return;
      }
    }

    var set = header.ShortTermReferencePictureSet;
    var maxLsb = header.Sps.MaxPicOrderCntLsb;

    var before = new List<H265Picture>();
    var after = new List<H265Picture>();
    var longTerm = new List<H265Picture>();
    var kept = new HashSet<H265Picture>();

    for (var i = 0; i < set.NegativeCount; ++i) {
      var target = poc + set.DeltaPocS0[i];
      var picture = this._FindShortTerm(target);

      if (set.UsedByCurrPicS0[i])
        before.Add(picture ?? _Missing(target, "before"));

      if (picture != null)
        kept.Add(picture);
    }

    for (var i = 0; i < set.PositiveCount; ++i) {
      var target = poc + set.DeltaPocS1[i];
      var picture = this._FindShortTerm(target);

      if (set.UsedByCurrPicS1[i])
        after.Add(picture ?? _Missing(target, "after"));

      if (picture != null)
        kept.Add(picture);
    }

    var msbCycle = 0;
    for (var i = 0; i < header.LongTermPocLsb.Length; ++i) {
      msbCycle = i == 0 ? header.LongTermMsbCycle[i] : msbCycle + header.LongTermMsbCycle[i];

      var target = header.LongTermPocLsb[i];
      H265Picture? picture;

      if (header.LongTermMsbPresent[i]) {
        target += poc - msbCycle * maxLsb - (poc & (maxLsb - 1));
        picture = this._Find(target);
      } else
        picture = this._FindByLowBits(target, maxLsb);

      if (picture != null) {
        picture.Marking = H265ReferenceMarking.LongTerm;
        kept.Add(picture);
      }

      if (header.LongTermUsedByCurrentPicture[i])
        longTerm.Add(picture ?? _Missing(target, "as a long-term reference"));
    }

    foreach (var picture in this._buffer)
      if (!kept.Contains(picture))
        picture.Marking = H265ReferenceMarking.Unused;
      else if (picture.Marking != H265ReferenceMarking.LongTerm)
        picture.Marking = H265ReferenceMarking.ShortTerm;

    this.ShortTermBefore = [.. before];
    this.ShortTermAfter = [.. after];
    this.LongTermCurrent = [.. longTerm];

    this._buffer.RemoveAll(static picture
      => picture.Marking == H265ReferenceMarking.Unused && (picture.WasOutput || !picture.IsOutput));
  }

  private static H265Picture _Missing(int poc, string where)
    => throw new InvalidDataException(
      $"An H.265 slice names a reference picture with order count {poc} {where} the current one, and the decoded "
      + "picture buffer does not hold it. Either decoding began somewhere other than a random access point, or a "
      + "picture was lost. Decoding on without it would predict from the wrong picture.");

  /// <summary>
  /// Builds the two lists a slice indexes its references by — clause 8.3.4.
  /// </summary>
  /// <remarks>
  /// The first list runs backwards in time then forwards, and the second the other way about, so
  /// that index zero of each names the nearest picture in the direction that list is for. A list
  /// shorter than the slice activates is filled by repeating itself, which is not a mistake: it lets
  /// a slice name the same picture twice with different weights.
  /// </remarks>
  internal IReadOnlyList<H265Picture>[] BuildLists(H265SliceHeader header) {
    ArgumentNullException.ThrowIfNull(header);

    if (header.SliceType == H265SliceType.I)
      return [[], []];

    var list0 = _Build(this.ShortTermBefore, this.ShortTermAfter, this.LongTermCurrent,
      header.NumRefIdxL0Active, header.ListEntryL0);

    var list1 = header.SliceType == H265SliceType.B
      ? _Build(this.ShortTermAfter, this.ShortTermBefore, this.LongTermCurrent,
        header.NumRefIdxL1Active, header.ListEntryL1)
      : [];

    return [list0, list1];
  }

  private static H265Picture[] _Build(
    H265Picture[] first, H265Picture[] second, H265Picture[] longTerm, int active, int[]? entries) {
    var available = first.Length + second.Length + longTerm.Length;
    if (available == 0)
      throw new InvalidDataException(
        "An H.265 predicted slice names no reference pictures at all. A slice that predicts from nothing has no "
        + "defined reconstruction.");

    var temporary = new H265Picture[Math.Max(active, available)];
    var at = 0;
    while (at < temporary.Length) {
      foreach (var picture in first)
        if (at < temporary.Length)
          temporary[at++] = picture;

      foreach (var picture in second)
        if (at < temporary.Length)
          temporary[at++] = picture;

      foreach (var picture in longTerm)
        if (at < temporary.Length)
          temporary[at++] = picture;
    }

    var list = new H265Picture[active];
    for (var i = 0; i < active; ++i) {
      var index = entries != null ? entries[i] : i;
      if (index >= temporary.Length)
        throw new InvalidDataException(
          $"An H.265 slice reorders its reference list to entry {index}, past the {temporary.Length} pictures the "
          + "reference picture set names.");

      list[i] = temporary[index];
    }

    return list;
  }

  /// <summary>Records a finished picture and hands out whatever may now be shown.</summary>
  internal void Add(H265Picture picture, H265SliceHeader header, IReadOnlyList<H265Picture>[] lists) {
    ArgumentNullException.ThrowIfNull(picture);
    ArgumentNullException.ThrowIfNull(header);
    ArgumentNullException.ThrowIfNull(lists);

    // What this picture's own references were, so that a later picture borrowing its motion can
    // scale the vectors by the distances they were measured over.
    picture.ReferencePictureOrderCounts = [
      _OrderCounts(lists[0]), _OrderCounts(lists[1]),
    ];
    picture.ReferenceIsLongTerm = [_LongTermFlags(lists[0]), _LongTermFlags(lists[1])];

    picture.Marking = header.Nal.IsSubLayerReference
      ? H265ReferenceMarking.ShortTerm
      : H265ReferenceMarking.Unused;

    this._buffer.Add(picture);
    this._sawFirstPicture = true;

    if (header.Nal.TemporalId == 0 && header.Nal.IsSubLayerReference && !_IsLeading(header.Nal)) {
      this._previousPocLsb = header.PicOrderCntLsb;
      this._previousPocMsb = picture.PictureOrderCount - header.PicOrderCntLsb;
    }
  }

  private static bool _IsLeading(H265NalUnit nal)
    => nal.Type is H265NalUnitType.RandomAccessDecodableLeadingNonReference
      or H265NalUnitType.RandomAccessDecodableLeadingReference
      or H265NalUnitType.RandomAccessSkippedLeadingNonReference
      or H265NalUnitType.RandomAccessSkippedLeadingReference;

  private static int[] _OrderCounts(IReadOnlyList<H265Picture> list) {
    var counts = new int[list.Count];
    for (var i = 0; i < list.Count; ++i)
      counts[i] = list[i].PictureOrderCount;

    return counts;
  }

  private static bool[] _LongTermFlags(IReadOnlyList<H265Picture> list) {
    var flags = new bool[list.Count];
    for (var i = 0; i < list.Count; ++i)
      flags[i] = list[i].Marking == H265ReferenceMarking.LongTerm;

    return flags;
  }

  /// <summary>Whether a decoded picture is ready to be shown, and which.</summary>
  internal bool TryTakeOutput(out H265Picture picture) {
    if (this._output.Count == 0) {
      picture = null!;
      return false;
    }

    picture = this._output.Dequeue();
    return true;
  }

  /// <summary>Everything still held, in the order it is to be shown.</summary>
  internal IEnumerable<H265Picture> Flush() {
    this._DrainOutput();

    while (this._output.Count > 0)
      yield return this._output.Dequeue();
  }

  /// <summary>
  /// Releases pictures that can no longer be overtaken — Annex C.5.2.2 and C.5.2.4.
  /// </summary>
  /// <remarks>
  /// A picture may be shown once no picture still to be decoded can belong before it, and the
  /// sequence parameter set says how many may be waiting at once. Holding to that number rather than
  /// to the whole buffer is what keeps the delay bounded: a stream that states it reorders by two
  /// never makes a decoder wait for three.
  /// <para/>
  /// <b>This runs before the current picture is decoded, not after it is stored,</b> and the
  /// difference is the whole of what makes the output order right. Both of the standard's conditions
  /// count the buffer as it stands with the current picture <em>not yet in it</em> and with the
  /// pictures its own reference picture set has just released already gone. Run it a picture later
  /// and the buffer is one fuller and holds pictures that are about to be dropped, so the second
  /// condition fires early and shows a picture that a picture still to be decoded belongs before —
  /// which is two frames of a film swapped, and nothing that looks like an error.
  /// </remarks>
  internal void BumpBeforeDecoding(int maxReorder, int maxBuffering) {
    while (true) {
      var waiting = 0;
      foreach (var picture in this._buffer)
        if (picture.IsOutput && !picture.WasOutput)
          ++waiting;

      if (waiting == 0)
        return;

      if (waiting <= maxReorder && this._buffer.Count < Math.Max(maxBuffering, 1))
        return;

      this._EmitEarliest();

      this._buffer.RemoveAll(static picture
        => picture.Marking == H265ReferenceMarking.Unused && (picture.WasOutput || !picture.IsOutput));
    }
  }

  private void _DrainOutput() {
    while (true) {
      var waiting = false;
      foreach (var picture in this._buffer)
        if (picture.IsOutput && !picture.WasOutput) {
          waiting = true;
          break;
        }

      if (!waiting)
        return;

      this._EmitEarliest();
    }
  }

  private void _EmitEarliest() {
    H265Picture? earliest = null;
    foreach (var picture in this._buffer)
      if (picture.IsOutput && !picture.WasOutput
          && (earliest == null || picture.PictureOrderCount < earliest.PictureOrderCount))
        earliest = picture;

    if (earliest == null)
      return;

    earliest.WasOutput = true;
    this._output.Enqueue(earliest);
  }

  private H265Picture? _FindShortTerm(int poc) {
    foreach (var picture in this._buffer)
      if (picture.PictureOrderCount == poc && picture.Marking != H265ReferenceMarking.Unused)
        return picture;

    return null;
  }

  private H265Picture? _Find(int poc) {
    foreach (var picture in this._buffer)
      if (picture.PictureOrderCount == poc)
        return picture;

    return null;
  }

  private H265Picture? _FindByLowBits(int pocLsb, int maxLsb) {
    foreach (var picture in this._buffer)
      if ((picture.PictureOrderCount & (maxLsb - 1)) == pocLsb)
        return picture;

    return null;
  }
}
