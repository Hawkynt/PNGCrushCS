using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>Decoded-picture-buffer reference marking and list construction for progressive H.264 frames.</summary>
internal sealed class H264ReferencePictures {
  private readonly List<H264Picture> _shortTerm = [];
  private readonly List<H264Picture> _longTerm = [];
  private long _nextSerial = 1;

  internal long TakeSerial() => this._nextSerial++;
  internal int? PreviousReferenceFrameNum { get; private set; }

  internal void Clear() {
    this._shortTerm.Clear();
    this._longTerm.Clear();
    this.PreviousReferenceFrameNum = null;
  }

  internal H264Picture[] BuildList0(H264SliceHeader header)
    => this.BuildLists(header, 0).L0;

  /// <summary>Builds and modifies both reference lists. List1 is empty outside B slices.</summary>
  internal (H264Picture[] L0, H264Picture[] L1) BuildLists(H264SliceHeader header, int currentPicOrderCnt) {
    if (header.IsIntra)
      return ([], []);

    var maxFrameNum = header.Sps.MaxFrameNum;
    this._UpdateShortTermPicNums(header.FrameNum, maxFrameNum);

    if (!header.IsB) {
      var list0 = new List<H264Picture>(this._shortTerm.Count + this._longTerm.Count);
      var shortTerm = new List<H264Picture>(this._shortTerm);
      shortTerm.Sort(static (a, b) => b.PicNum.CompareTo(a.PicNum));
      list0.AddRange(shortTerm);
      _AppendLongTerm(list0, this._longTerm);
      _RequireReferences(list0, "P");
      _Modify(list0, header.ListModificationsL0, header, maxFrameNum, 0);
      _Truncate(list0, header.NumRefIdxL0Active);
      return ([.. list0], []);
    }

    var before = new List<H264Picture>();
    var after = new List<H264Picture>();
    foreach (var picture in this._shortTerm)
      if (picture.PicOrderCnt < currentPicOrderCnt)
        before.Add(picture);
      else
        after.Add(picture);

    before.Sort(static (a, b) => b.PicOrderCnt.CompareTo(a.PicOrderCnt));
    after.Sort(static (a, b) => a.PicOrderCnt.CompareTo(b.PicOrderCnt));

    var l0 = new List<H264Picture>(this._shortTerm.Count + this._longTerm.Count);
    l0.AddRange(before);
    l0.AddRange(after);
    _AppendLongTerm(l0, this._longTerm);

    var l1 = new List<H264Picture>(this._shortTerm.Count + this._longTerm.Count);
    l1.AddRange(after);
    l1.AddRange(before);
    _AppendLongTerm(l1, this._longTerm);

    _RequireReferences(l0, "B");
    if (l1.Count > 1 && _SameOrder(l0, l1))
      (l1[0], l1[1]) = (l1[1], l1[0]);

    _Modify(l0, header.ListModificationsL0, header, maxFrameNum, 0);
    _Modify(l1, header.ListModificationsL1, header, maxFrameNum, 1);
    _Truncate(l0, header.NumRefIdxL0Active);
    _Truncate(l1, header.NumRefIdxL1Active);
    return ([.. l0], [.. l1]);
  }

  private static void _AppendLongTerm(List<H264Picture> target, List<H264Picture> source) {
    var ordered = new List<H264Picture>(source);
    ordered.Sort(static (a, b) => a.LongTermPicNum.CompareTo(b.LongTermPicNum));
    target.AddRange(ordered);
  }

  private static void _RequireReferences(List<H264Picture> list, string slice) {
    if (list.Count == 0)
      throw new InvalidDataException(
        $"An H.264 {slice} slice was reached with no reference picture in the decoded picture buffer. Decoding must begin at an IDR picture.");
  }

  private static bool _SameOrder(List<H264Picture> first, List<H264Picture> second) {
    if (first.Count != second.Count)
      return false;
    for (var i = 0; i < first.Count; ++i)
      if (first[i].Serial != second[i].Serial)
        return false;
    return true;
  }

  private static void _Truncate(List<H264Picture> list, int active) {
    if (list.Count > active)
      list.RemoveRange(active, list.Count - active);
  }

  private void _UpdateShortTermPicNums(int currentFrameNum, int maxFrameNum) {
    foreach (var picture in this._shortTerm)
      picture.PicNum = picture.FrameNum > currentFrameNum ? picture.FrameNum - maxFrameNum : picture.FrameNum;
  }

  private static void _Modify(
    List<H264Picture> list,
    IReadOnlyList<H264ListModification> modifications,
    H264SliceHeader header,
    int maxFrameNum,
    int listNumber) {
    if (modifications.Count == 0)
      return;

    var predicted = header.FrameNum;
    var target = 0;
    foreach (var modification in modifications) {
      int found;
      if (modification.Idc == 2) {
        var longTermPicNum = modification.Value;
        found = list.FindIndex(p => p.IsLongTerm && p.LongTermPicNum == longTermPicNum);
        if (found < 0)
          throw new InvalidDataException(
            $"An H.264 slice reorders reference list {listNumber} to long-term picture {longTermPicNum}, but the DPB holds no such picture.");
      } else {
        var difference = modification.Value + 1;
        var noWrap = modification.Idc == 0
          ? predicted - difference < 0 ? predicted - difference + maxFrameNum : predicted - difference
          : predicted + difference >= maxFrameNum ? predicted + difference - maxFrameNum : predicted + difference;
        predicted = noWrap;
        var picNum = noWrap > header.FrameNum ? noWrap - maxFrameNum : noWrap;
        found = list.FindIndex(p => !p.IsLongTerm && p.PicNum == picNum);
        if (found < 0)
          throw new InvalidDataException(
            $"An H.264 slice reorders reference list {listNumber} to PicNum {picNum}, but the DPB holds no such short-term picture.");
      }

      var moved = list[found];
      list.RemoveAt(found);
      list.Insert(Math.Min(target, list.Count), moved);
      ++target;
    }
  }

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

  private void _AddLongTerm(H264Picture picture, int index) {
    this._RemoveLongTermAt(index);
    picture.IsLongTerm = true;
    picture.LongTermFrameIdx = index;
    this._longTerm.Add(picture);
  }

  private void _RemoveLongTermAt(int index) {
    var found = this._longTerm.FindIndex(p => p.LongTermFrameIdx == index);
    if (found >= 0)
      this._longTerm.RemoveAt(found);
  }

  private void _SlideWindow(H264SequenceParameterSet sps) {
    var capacity = Math.Max(sps.MaxNumRefFrames, 1);
    while (this._shortTerm.Count + this._longTerm.Count >= capacity) {
      if (this._shortTerm.Count == 0)
        throw new InvalidDataException(
          $"The H.264 DPB is filled by {this._longTerm.Count} long-term references; sliding-window marking cannot discard one.");
      var oldest = 0;
      for (var i = 1; i < this._shortTerm.Count; ++i)
        if (this._shortTerm[i].PicNum < this._shortTerm[oldest].PicNum)
          oldest = i;
      this._shortTerm.RemoveAt(oldest);
    }
  }

  private void _ApplyMarking(H264SliceHeader header, ref int currentLongTermFrameIdx, ref bool resetFrameNumber) {
    foreach (var operation in header.MarkingOperations)
      switch (operation.Operation) {
        case 1: {
          var picNum = _ShortTermPicNumFromDifference(header, operation.First);
          var found = this._shortTerm.FindIndex(p => p.PicNum == picNum);
          if (found < 0)
            throw new InvalidDataException($"H.264 MMCO 1 names missing short-term PicNum {picNum}.");
          this._shortTerm.RemoveAt(found);
          break;
        }
        case 2: {
          var found = this._longTerm.FindIndex(p => p.LongTermPicNum == operation.First);
          if (found < 0)
            throw new InvalidDataException($"H.264 MMCO 2 names missing long-term picture {operation.First}.");
          this._longTerm.RemoveAt(found);
          break;
        }
        case 3: {
          var picNum = _ShortTermPicNumFromDifference(header, operation.First);
          var found = this._shortTerm.FindIndex(p => p.PicNum == picNum);
          if (found < 0)
            throw new InvalidDataException($"H.264 MMCO 3 names missing short-term PicNum {picNum}.");
          var moved = this._shortTerm[found];
          this._shortTerm.RemoveAt(found);
          this._AddLongTerm(moved, operation.Second);
          break;
        }
        case 4: {
          var max = operation.First - 1;
          this._longTerm.RemoveAll(p => p.LongTermFrameIdx > max);
          if (currentLongTermFrameIdx > max)
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
          currentLongTermFrameIdx = operation.Second;
          this._RemoveLongTermAt(currentLongTermFrameIdx);
          break;
      }
  }

  private static int _ShortTermPicNumFromDifference(H264SliceHeader header, int differenceMinus1) {
    var maxFrameNum = header.Sps.MaxFrameNum;
    var noWrap = header.FrameNum - (differenceMinus1 + 1);
    if (noWrap < 0)
      noWrap += maxFrameNum;
    return noWrap > header.FrameNum ? noWrap - maxFrameNum : noWrap;
  }
}
