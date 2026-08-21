using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>
/// The decoded picture buffer: which pictures are still references, in what order a slice sees them,
/// and when one stops being one — ITU-T H.264, clause 8.2.4 and 8.2.5.
/// </summary>
/// <remarks>
/// Short-term references only. A long-term reference is a picture an encoder asks to be kept past
/// the point the sliding window would have dropped it, and the machinery for that — a second
/// numbering, a second sort key in the list, and four of the six memory management operations — is
/// refused by name here rather than written and left untested. Nothing this decoder is aimed at
/// produces one: neither x264 at any preset nor any Baseline profile encoder in ordinary use.
/// <para/>
/// The ordering rule for P slices reads oddly until the wrap-around is taken into account. Pictures
/// come out of the buffer most recently coded first, and "most recently" is decided by
/// <c>PicNum</c> — <c>frame_num</c> shifted so that pictures coded before the current one are below
/// it even when the counter has wrapped through zero in between (clause 8.2.4.1). Sorting on
/// <c>frame_num</c> itself would, once per wrap, hand a slice its references in exactly the wrong
/// order.
/// </remarks>
internal sealed class H264ReferencePictures {

  private readonly List<H264Picture> _shortTerm = [];
  private long _nextSerial = 1;

  /// <summary>Takes a serial for a newly started picture.</summary>
  internal long TakeSerial() => this._nextSerial++;

  /// <summary>The <c>frame_num</c> of the most recent reference picture, for the gap check.</summary>
  internal int? PreviousReferenceFrameNum { get; private set; }

  /// <summary>Forgets every reference, which is what an IDR picture does (clause 8.2.5.1).</summary>
  internal void Clear() {
    this._shortTerm.Clear();
    this.PreviousReferenceFrameNum = null;
  }

  /// <summary>
  /// Builds reference picture list 0 for one slice: the initialisation of clause 8.2.4.2.1 followed
  /// by the modifications of clause 8.2.4.3.1.
  /// </summary>
  internal H264Picture[] BuildList0(H264SliceHeader header) {
    if (header.IsIntra)
      return [];

    var maxFrameNum = header.Sps.MaxFrameNum;

    // PicNum is relative to the picture being decoded now, so it is recomputed for every slice
    // rather than stored once when the reference was decoded (clause 8.2.4.1).
    foreach (var picture in this._shortTerm)
      picture.PicNum = picture.FrameNum > header.FrameNum ? picture.FrameNum - maxFrameNum : picture.FrameNum;

    var list = new List<H264Picture>(this._shortTerm);
    list.Sort(static (first, second) => second.PicNum.CompareTo(first.PicNum));

    if (list.Count == 0)
      throw new InvalidDataException(
        "An H.264 P slice was reached with no reference picture in the decoded picture buffer. Decoding must begin "
        + "at an IDR picture, and every picture the stream predicts from must have been decoded.");

    // The list is not padded out to the length the slice states. Clause 8.2.4.2 builds it from the
    // pictures that exist and discards any beyond that length; where there are fewer, the entries
    // past the end are left undefined for the modification below to fill in. Repeating a picture to
    // fill them would put a second copy of it in the list, which the modification then finds instead
    // of the one it meant — and would turn a stream that names a reference it never sent into a
    // picture predicted from the wrong one rather than into a refusal.
    _Modify(list, header, maxFrameNum);

    if (list.Count > header.NumRefIdxL0Active)
      list.RemoveRange(header.NumRefIdxL0Active, list.Count - header.NumRefIdxL0Active);

    return [.. list];
  }

  /// <summary>The reference picture list modification of clause 8.2.4.3.1.</summary>
  private static void _Modify(List<H264Picture> list, H264SliceHeader header, int maxFrameNum) {
    if (header.ListModificationsL0.Count == 0)
      return;

    var predicted = header.FrameNum;
    var target = 0;

    foreach (var modification in header.ListModificationsL0) {
      if (modification.Idc == 2)
        throw new NotSupportedException(
          "This H.264 slice reorders its reference picture list by long-term picture number "
          + "(modification_of_pic_nums_idc 2, H.264 clause 8.2.4.3.1). Long-term references are not implemented.");

      // The differences are coded relative to the last one applied rather than to the current
      // picture, so a slice naming several references spends few bits on each.
      var difference = modification.Value + 1;
      var noWrap = modification.Idc == 0
        ? predicted - difference < 0 ? predicted - difference + maxFrameNum : predicted - difference
        : predicted + difference >= maxFrameNum ? predicted + difference - maxFrameNum : predicted + difference;

      predicted = noWrap;
      var picNum = noWrap > header.FrameNum ? noWrap - maxFrameNum : noWrap;

      var found = list.FindIndex(picture => picture.PicNum == picNum);
      if (found < 0)
        throw new InvalidDataException(
          $"An H.264 slice reorders its reference picture list to put PicNum {picNum} at index {target}, and no "
          + "picture in the decoded picture buffer has that number. The stream refers to a picture that was never "
          + "decoded.");

      var moved = list[found];
      list.RemoveAt(found);
      list.Insert(Math.Min(target, list.Count), moved);
      ++target;
    }
  }

  /// <summary>
  /// Files a decoded reference picture, dropping whichever one it displaces — clause 8.2.5.
  /// </summary>
  internal void Add(H264Picture picture, H264SliceHeader header) {
    if (header.IdrPicFlag) {
      if (header.LongTermReferenceFlag)
        throw new NotSupportedException(
          "This H.264 IDR picture sets long_term_reference_flag, so it is kept as a long-term reference "
          + "(H.264, clause 8.2.5.1). Long-term references are not implemented.");

      this._shortTerm.Clear();
      this._shortTerm.Add(picture);
      this.PreviousReferenceFrameNum = picture.FrameNum;
      return;
    }

    if (header.AdaptiveRefPicMarkingModeFlag)
      this._ApplyMarking(header);
    else
      this._SlideWindow(header.Sps);

    this._shortTerm.Add(picture);
    this.PreviousReferenceFrameNum = picture.FrameNum;
  }

  /// <summary>
  /// The sliding window: the oldest reference falls out when the buffer is full — clause 8.2.5.3.
  /// </summary>
  private void _SlideWindow(H264SequenceParameterSet sps) {
    var capacity = Math.Max(sps.MaxNumRefFrames, 1);
    while (this._shortTerm.Count >= capacity) {
      var oldest = 0;
      for (var i = 1; i < this._shortTerm.Count; ++i)
        if (this._shortTerm[i].PicNum < this._shortTerm[oldest].PicNum)
          oldest = i;

      this._shortTerm.RemoveAt(oldest);
    }
  }

  /// <summary>The memory management control operations a slice may carry instead — clause 8.2.5.4.</summary>
  private void _ApplyMarking(H264SliceHeader header) {
    foreach (var operation in header.MarkingOperations)
      switch (operation.Operation) {
        case 1: {
          // Mark one short-term picture unused, named by how far below the current one it is.
          var picNum = header.FrameNum - (operation.First + 1);
          var found = this._shortTerm.FindIndex(picture => picture.PicNum == picNum);
          if (found < 0)
            throw new InvalidDataException(
              $"An H.264 slice marks PicNum {picNum} as unused for reference (memory_management_control_operation "
              + "1), and no picture in the decoded picture buffer has that number.");

          this._shortTerm.RemoveAt(found);
          break;
        }

        case 4 when operation.First == 0:
          // "No long-term references from here on", which for a buffer that has never held one is
          // nothing at all. Encoders emit it to state the intent rather than to change anything.
          break;

        default:
          throw new NotSupportedException(
            $"This H.264 slice carries memory_management_control_operation {operation.Operation} "
            + $"({_MarkingName(operation.Operation)}, H.264 Table 7-9). This decoder implements the sliding window "
            + "and operation 1 only; long-term references and the reference reset are not implemented.");
      }
  }

  private static string _MarkingName(int operation) => operation switch {
    2 => "mark a long-term picture unused",
    3 => "turn a short-term picture into a long-term one",
    4 => "set the largest long-term frame index",
    5 => "mark every reference picture unused and reset the frame numbering",
    6 => "keep the current picture as a long-term reference",
    _ => "reserved",
  };
}
