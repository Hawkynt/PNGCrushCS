using System;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>
/// Persistent picture-order-count derivation for progressive H.264 frame pictures (clause 8.2.1).
/// </summary>
/// <remarks>
/// The three POC algorithms are deliberately kept independent from the DPB. POC is derived before a
/// B slice's reference lists can be built, while MMCO-5 is applied only after the picture has been
/// decoded. <see cref="FinishPicture"/> therefore records both scopes the standard distinguishes:
/// type 0 needs to know whether the previous <em>reference</em> picture executed MMCO-5, while types
/// 1 and 2 need the same fact for the immediately previous picture in decoding order. The former must
/// survive intervening non-reference pictures; the latter must not.
/// <para/>
/// The equations are a C# adaptation of the corresponding implementation in OxideAV/oxideav-h264,
/// Copyright (c) 2026 Karpeles Lab Inc., used under the MIT License. The implementation is restricted
/// here to frame pictures because <see cref="H264SequenceParameterSet.RefuseUnsupported"/> rejects
/// field/MBAFF streams before a slice reaches this class.
/// </remarks>
internal sealed class H264PictureOrderCount {
  private int _prevPicOrderCntMsb;
  private int _prevPicOrderCntLsb;
  private int _prevFrameNum;
  private long _prevFrameNumOffset;
  private bool _previousPictureHadMmco5;
  private bool _previousReferenceHadMmco5;
  private int _previousReferenceTopFieldOrderCnt;

  internal readonly record struct Result(int TopFieldOrderCnt, int BottomFieldOrderCnt, int PicOrderCnt);

  internal Result Derive(H264SliceHeader header) {
    ArgumentNullException.ThrowIfNull(header);
    if (!header.Sps.FrameMbsOnlyFlag)
      throw new InvalidOperationException("POC derivation reached a field-coded H.264 picture after field coding was refused.");

    return header.Sps.PicOrderCntType switch {
      0 => this._DeriveType0(header),
      1 => this._DeriveType1(header),
      2 => this._DeriveType2(header),
      var type => throw new InvalidDataException($"H.264 pic_order_cnt_type {type} is outside 0 through 2."),
    };
  }

  /// <summary>Applies post-picture MMCO-5 POC normalization and records state for the next picture.</summary>
  internal Result FinishPicture(H264SliceHeader header, Result result) {
    var hasMmco5 = false;
    if (header.IsReference)
      foreach (var operation in header.MarkingOperations)
        if (operation.Operation == 5) {
          hasMmco5 = true;
          break;
        }

    if (hasMmco5) {
      var shift = result.PicOrderCnt;
      var top = _Checked((long)result.TopFieldOrderCnt - shift);
      var bottom = _Checked((long)result.BottomFieldOrderCnt - shift);
      result = new(top, bottom, Math.Min(top, bottom));
    }

    this._previousPictureHadMmco5 = hasMmco5;
    if (header.IsReference) {
      this._previousReferenceHadMmco5 = hasMmco5;
      if (hasMmco5)
        this._previousReferenceTopFieldOrderCnt = result.TopFieldOrderCnt;
    }

    return result;
  }

  internal void Reset() {
    this._prevPicOrderCntMsb = 0;
    this._prevPicOrderCntLsb = 0;
    this._prevFrameNum = 0;
    this._prevFrameNumOffset = 0;
    this._previousPictureHadMmco5 = false;
    this._previousReferenceHadMmco5 = false;
    this._previousReferenceTopFieldOrderCnt = 0;
  }

  private Result _DeriveType0(H264SliceHeader header) {
    var sps = header.Sps;
    var maxLsb = 1 << sps.Log2MaxPicOrderCntLsb;

    int prevMsb;
    int prevLsb;
    if (header.IdrPicFlag) {
      prevMsb = 0;
      prevLsb = 0;
    } else if (this._previousReferenceHadMmco5) {
      prevMsb = 0;
      prevLsb = this._previousReferenceTopFieldOrderCnt;
    } else {
      prevMsb = this._prevPicOrderCntMsb;
      prevLsb = this._prevPicOrderCntLsb;
    }

    var currentLsb = header.PicOrderCntLsb;
    long msb = prevMsb;
    if (currentLsb < prevLsb && prevLsb - currentLsb >= maxLsb / 2)
      msb += maxLsb;
    else if (currentLsb > prevLsb && currentLsb - prevLsb > maxLsb / 2)
      msb -= maxLsb;

    var top = _Checked(msb + currentLsb);
    var bottom = _Checked((long)top + header.DeltaPicOrderCntBottom);
    var result = new Result(top, bottom, Math.Min(top, bottom));

    // Clause 8.2.1.1 anchors wrap detection to the previous reference picture only.
    if (header.IsReference) {
      this._prevPicOrderCntMsb = _Checked(msb);
      this._prevPicOrderCntLsb = currentLsb;
    }
    this._prevFrameNum = header.FrameNum;
    return result;
  }

  private Result _DeriveType1(H264SliceHeader header) {
    var sps = header.Sps;
    var frameNumOffset = this._FrameNumOffset(header);
    long absFrameNum = sps.OffsetForRefFrame.Length != 0 ? frameNumOffset + header.FrameNum : 0;
    if (!header.IsReference && absFrameNum > 0)
      --absFrameNum;

    long expected = 0;
    if (absFrameNum > 0) {
      var cycleLength = sps.OffsetForRefFrame.Length;
      var cycleDelta = 0L;
      foreach (var offset in sps.OffsetForRefFrame)
        cycleDelta += offset;

      var cycleCount = (absFrameNum - 1) / cycleLength;
      var inCycle = (int)((absFrameNum - 1) % cycleLength);
      expected = cycleCount * cycleDelta;
      for (var i = 0; i <= inCycle; ++i)
        expected += sps.OffsetForRefFrame[i];
    }
    if (!header.IsReference)
      expected += sps.OffsetForNonRefPic;

    var delta0 = sps.DeltaPicOrderAlwaysZeroFlag ? 0 : header.DeltaPicOrderCnt0;
    var delta1 = sps.DeltaPicOrderAlwaysZeroFlag ? 0 : header.DeltaPicOrderCnt1;
    var top = _Checked(expected + delta0);
    var bottom = _Checked((long)top + sps.OffsetForTopToBottomField + delta1);

    this._prevFrameNum = header.FrameNum;
    this._prevFrameNumOffset = frameNumOffset;
    return new(top, bottom, Math.Min(top, bottom));
  }

  private Result _DeriveType2(H264SliceHeader header) {
    var frameNumOffset = this._FrameNumOffset(header);
    long temporary;
    if (header.IdrPicFlag)
      temporary = 0;
    else if (!header.IsReference)
      temporary = 2 * (frameNumOffset + header.FrameNum) - 1;
    else
      temporary = 2 * (frameNumOffset + header.FrameNum);

    var poc = _Checked(temporary);
    this._prevFrameNum = header.FrameNum;
    this._prevFrameNumOffset = frameNumOffset;
    return new(poc, poc, poc);
  }

  private long _FrameNumOffset(H264SliceHeader header) {
    if (header.IdrPicFlag)
      return 0;

    var previousOffset = this._previousPictureHadMmco5 ? 0 : this._prevFrameNumOffset;
    var previousFrameNum = this._previousPictureHadMmco5 ? 0 : this._prevFrameNum;
    return previousFrameNum > header.FrameNum
      ? previousOffset + header.Sps.MaxFrameNum
      : previousOffset;
  }

  private static int _Checked(long value) {
    if (value is < int.MinValue or > int.MaxValue)
      throw new InvalidDataException("H.264 picture order count overflowed the signed 32-bit range.");
    return (int)value;
  }
}
