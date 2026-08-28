using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>
/// The decoded picture buffer: which pictures are still references, in what order a slice sees them,
/// and when one stops being one — ITU-T H.264, clauses 8.2.4 and 8.2.5.
/// </summary>
/// <remarks>
/// Both short- and long-term frame references are retained. Short-term pictures are named by
/// <c>PicNum</c>, relative to the frame currently being decoded; long-term pictures have the stable
/// <c>LongTermFrameIdx</c>/<c>LongTermPicNum</c> assigned by memory-management control operations.
/// Because this decoder accepts frame pictures only, the field-picture variants of LongTermPicNum do
/// not arise.
/// </remarks>
internal sealed class H264ReferencePictures {

  private readonly List<H264Picture> _shortTerm = [];
  private readonly List<H264Picture> _longTerm = [];
  private long _nextSerial = 1;

  /// <summary>Takes a serial for a newly started picture.</summary>
  internal long TakeSerial() => this._nextSerial++;

  /// <summary>The <c>frame_num</c> of the most recent reference picture, for the gap check.</summary>
  internal int? PreviousReferenceFrameNum { get; private set; }

  /// <summary>Forgets every reference, which is what an IDR picture does (clause 8.2.5.1).</summary>
  internal void Clear() {
    this._shortTerm.Clear();
    this._longTerm.Clear();
    this.PreviousReferenceFrameNum = null;
  }

  /// <summary>
  /// Builds reference picture list 0 for one P slice: short-term references in descending PicNum,
  /// followed by long-term references in ascending LongTermPicNum, then the slice's explicit list
  /// modifications — clauses 8.2.4.2.1 and 8.2.4.3.1.
  /// </summary>
  internal H264Picture[] BuildList0(H264SliceHeader header) {
    if (header.IsIntra)
      return [];

    var maxFrameNum = header.Sps.MaxFrameNum;
    this._UpdateShortTermPicNums(header.FrameNum, maxFrameNum);

    var list = new List<H264Picture>(this._shortTerm.Count + this._longTerm.Count);

    var shortTerm = new List<H264Picture>(this._shortTerm);
    shortTerm.Sort(static (first, second) => second.PicNum.CompareTo(first.PicNum));
    list.AddRange(shortTerm);

    var longTerm = new List<H264Picture>(this._longTerm);
    longTerm.Sort(static (first, second) => first.LongTermPicNum.CompareTo(second.LongTermPicNum));
    list.AddRange(longTerm);

    if (list.Count == 0)
      throw new InvalidDataException(
        "An H.264 P slice was reached with no reference picture in the decoded picture buffer. Decoding must begin "
        + "at an IDR picture, and every picture the stream predicts from must have been decoded.");

    // The list is not padded out to the active length. Undefined entries are not real references and
    // fabricating duplicates there would let a damaged stream predict from the wrong picture.
    _Modify(list, header, maxFrameNum);

    if (list.Count > header.NumRefIdxL0Active)
      list.RemoveRange(header.NumRefIdxL0Active, list.Count - header.NumRefIdxL0Active);

    return [.. list];
  }

  private void _UpdateShortTermPicNums(int currentFrameNum, int maxFrameNum) {
    foreach (var picture in this._shortTerm)
      picture.PicNum = picture.FrameNum > currentFrameNum ? picture.FrameNum - maxFrameNum : picture.FrameNum;
  }

  /// <summary>The reference picture list modification of clause 8.2.4.3.1.</summary>
  private static void _Modify(List<H264Picture> list, H264SliceHeader header, int maxFrameNum) {
    if (header.ListModificationsL0.Count == 0)
      return;

    var predicted = header.FrameNum;
    var target = 0;

    foreach (var modification in header.ListModificationsL0) {
      int found;

      if (modification.Idc == 2) {
        var longTermPicNum = modification.Value;
        found = list.FindIndex(picture => picture.IsLongTerm && picture.LongTermPicNum == longTermPicNum);
        if (found < 0)
          throw new InvalidDataException(
            $"An H.264 slice reorders reference list 0 to put long-term picture {longTermPicNum} at index {target}, "
            + "and no long-term picture in the decoded picture buffer has that number.");
      } else {
        // The differences are coded relative to the last short-term modification rather than to the
        // current picture, so several nearby references remain cheap to name.
        var difference = modification.Value + 1;
        var noWrap = modification.Idc == 0
          ? predicted - difference < 0 ? predicted - difference + maxFrameNum : predicted - difference
          : predicted + difference >= maxFrameNum ? predicted + difference - maxFrameNum : predicted + difference;

        predicted = noWrap;
        var picNum = noWrap > header.FrameNum ? noWrap - maxFrameNum : noWrap;
        found = list.FindIndex(picture => !picture.IsLongTerm && picture.PicNum == picNum);
        if (found < 0)
          throw new InvalidDataException(
            $"An H.264 slice reorders its reference picture list to put PicNum {picNum} at index {target}, and no "
            + "short-term picture in the decoded picture buffer has that number. The stream refers to a picture "
            + "that was never decoded.");
      }

      var moved = list[found];
      list.RemoveAt(found);
      list.Insert(Math.Min(target, list.Count), moved);
      ++target;
    }
  }

  /// <summary>
  /// Files a decoded reference picture, dropping or reclassifying references according to clause 8.2.5.
  /// </summary>
  internal void Add(H264Picture picture, H264SliceHeader header) {
    if (header.IdrPicFlag) {
      this._shortTerm.Clear();
      this._longTerm.Clear();

      if (header.LongTermReferenceFlag)
        this._AddLongTerm(picture, 0);
      else
        this._AddShortTerm(picture);

      this.PreviousReferenceFrameNum = picture.FrameNum;
      return;
    }

    this._UpdateShortTermPicNums(header.FrameNum, header.Sps.MaxFrameNum);

    var currentLongTermFrameIdx = -1;
    var resetFrameNumber = false;

    if (header.AdaptiveRefPicMarkingModeFlag)
      this._ApplyMarking(header, ref currentLongTermFrameIdx, ref resetFrameNumber);
    else
      this._SlideWindow(header.Sps);

    if (resetFrameNumber)
      picture.FrameNum = 0;

    if (currentLongTermFrameIdx >= 0)
      this._AddLongTerm(picture, currentLongTermFrameIdx);
    else
      this._AddShortTerm(picture);

    this.PreviousReferenceFrameNum = picture.FrameNum;
  }

  private void _AddShortTerm(H264Picture picture) {
    picture.IsLongTerm = false;
    picture.LongTermFrameIdx = -1;
    this._shortTerm.Add(picture);
  }

  private void _AddLongTerm(H264Picture picture, int longTermFrameIdx) {
    this._RemoveLongTermAt(longTermFrameIdx);
    picture.IsLongTerm = true;
    picture.LongTermFrameIdx = longTermFrameIdx;
    this._longTerm.Add(picture);
  }

  private void _RemoveLongTermAt(int longTermFrameIdx) {
    var found = this._longTerm.FindIndex(picture => picture.LongTermFrameIdx == longTermFrameIdx);
    if (found >= 0)
      this._longTerm.RemoveAt(found);
  }

  /// <summary>
  /// The sliding window: the oldest short-term reference falls out when the complete DPB is full —
  /// clause 8.2.5.3. Long-term references never age out through this process.
  /// </summary>
  private void _SlideWindow(H264SequenceParameterSet sps) {
    var capacity = Math.Max(sps.MaxNumRefFrames, 1);
    while (this._shortTerm.Count + this._longTerm.Count >= capacity) {
      if (this._shortTerm.Count == 0)
        throw new InvalidDataException(
          $"The H.264 decoded picture buffer already holds {this._longTerm.Count} long-term reference picture(s), "
          + $"filling max_num_ref_frames={capacity}. Sliding-window marking cannot discard a long-term picture; the "
          + "stream must use adaptive reference-picture marking before adding another reference.");

      var oldest = 0;
      for (var i = 1; i < this._shortTerm.Count; ++i)
        if (this._shortTerm[i].PicNum < this._shortTerm[oldest].PicNum)
          oldest = i;

      this._shortTerm.RemoveAt(oldest);
    }
  }

  /// <summary>The memory management control operations of clause 8.2.5.4.</summary>
  private void _ApplyMarking(
    H264SliceHeader header,
    ref int currentLongTermFrameIdx,
    ref bool resetFrameNumber) {
    foreach (var operation in header.MarkingOperations)
      switch (operation.Operation) {
        case 1: {
          var picNum = _ShortTermPicNumFromDifference(header, operation.First);
          var found = this._shortTerm.FindIndex(picture => picture.PicNum == picNum);
          if (found < 0)
            throw new InvalidDataException(
              $"An H.264 slice marks PicNum {picNum} as unused for reference (memory_management_control_operation "
              + "1), and no short-term picture in the decoded picture buffer has that number.");

          this._shortTerm.RemoveAt(found);
          break;
        }

        case 2: {
          var longTermPicNum = operation.First;
          var found = this._longTerm.FindIndex(picture => picture.LongTermPicNum == longTermPicNum);
          if (found < 0)
            throw new InvalidDataException(
              $"An H.264 slice marks long-term picture {longTermPicNum} unused "
              + "(memory_management_control_operation 2), but the decoded picture buffer holds no such picture.");

          this._longTerm.RemoveAt(found);
          break;
        }

        case 3: {
          var picNum = _ShortTermPicNumFromDifference(header, operation.First);
          var found = this._shortTerm.FindIndex(picture => picture.PicNum == picNum);
          if (found < 0)
            throw new InvalidDataException(
              $"An H.264 slice converts PicNum {picNum} to long-term frame index {operation.Second} "
              + "(memory_management_control_operation 3), but the decoded picture buffer holds no such short-term "
              + "picture.");

          var moved = this._shortTerm[found];
          this._shortTerm.RemoveAt(found);
          this._AddLongTerm(moved, operation.Second);
          break;
        }

        case 4: {
          // max_long_term_frame_idx_plus1 == 0 means no long-term references survive. Otherwise every
          // index above max_long_term_frame_idx becomes unused immediately.
          var maxLongTermFrameIdx = operation.First - 1;
          this._longTerm.RemoveAll(picture => picture.LongTermFrameIdx > maxLongTermFrameIdx);
          if (currentLongTermFrameIdx > maxLongTermFrameIdx)
            currentLongTermFrameIdx = -1;
          break;
        }

        case 5:
          this._shortTerm.Clear();
          this._longTerm.Clear();
          currentLongTermFrameIdx = -1;
          resetFrameNumber = true;
          break;

        case 6:
          // The current picture is added only after every operation has been processed. Remember the
          // requested long-term index here; a later operation 4 or 5 in the same marking sequence may
          // still cancel it before the picture enters the DPB.
          currentLongTermFrameIdx = operation.Second;
          this._RemoveLongTermAt(currentLongTermFrameIdx);
          break;
      }
  }

  private static int _ShortTermPicNumFromDifference(H264SliceHeader header, int differenceOfPicNumsMinus1) {
    var maxFrameNum = header.Sps.MaxFrameNum;
    var noWrap = header.FrameNum - (differenceOfPicNumsMinus1 + 1);
    if (noWrap < 0)
      noWrap += maxFrameNum;
    return noWrap > header.FrameNum ? noWrap - maxFrameNum : noWrap;
  }
}
