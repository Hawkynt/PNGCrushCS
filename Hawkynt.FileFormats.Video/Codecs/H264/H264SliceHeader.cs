using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.H264;

internal enum H264SliceType { P = 0, B = 1, I = 2, SP = 3, SI = 4 }
internal readonly record struct H264ListModification(int Idc, int Value);
internal readonly record struct H264MarkingOperation(int Operation, int First, int Second);

/// <summary>Syntax and reconstruction state carried by one H.264 slice header.</summary>
internal sealed class H264SliceHeader {
  internal int FirstMbInSlice { get; private set; }
  internal H264SliceType SliceType { get; private set; }
  internal H264PictureParameterSet Pps { get; private set; } = null!;
  internal H264SequenceParameterSet Sps { get; private set; } = null!;
  internal int FrameNum { get; private set; }
  internal bool IdrPicFlag { get; private set; }
  internal int IdrPicId { get; private set; }
  internal int PicOrderCntLsb { get; private set; }
  internal int DeltaPicOrderCntBottom { get; private set; }
  internal int DeltaPicOrderCnt0 { get; private set; }
  internal int DeltaPicOrderCnt1 { get; private set; }
  internal int RedundantPicCnt { get; private set; }
  internal bool DirectSpatialMvPredFlag { get; private set; }
  internal int NumRefIdxL0Active { get; private set; }
  internal int NumRefIdxL1Active { get; private set; }
  internal IReadOnlyList<H264ListModification> ListModificationsL0 { get; private set; } = [];
  internal IReadOnlyList<H264ListModification> ListModificationsL1 { get; private set; } = [];
  internal H264PredictionWeights? PredictionWeights { get; private set; }
  internal bool LongTermReferenceFlag { get; private set; }
  internal bool AdaptiveRefPicMarkingModeFlag { get; private set; }
  internal IReadOnlyList<H264MarkingOperation> MarkingOperations { get; private set; } = [];
  internal int CabacInitIdc { get; private set; }
  internal int SliceQpY { get; private set; }
  internal int DisableDeblockingFilterIdc { get; private set; }
  internal int FilterOffsetA { get; private set; }
  internal int FilterOffsetB { get; private set; }
  internal bool IsReference { get; private set; }

  internal bool IsIntra => this.SliceType is H264SliceType.I or H264SliceType.SI;
  internal bool IsB => this.SliceType == H264SliceType.B;

  internal static H264SliceHeader Parse(
    ref H264BitReader reader,
    H264NalUnit nal,
    IReadOnlyDictionary<int, H264SequenceParameterSet> sequenceSets,
    IReadOnlyDictionary<int, H264PictureParameterSet> pictureSets) {
    var header = new H264SliceHeader {
      FirstMbInSlice = reader.ReadUnsignedExpGolomb(),
      IdrPicFlag = nal.IsIdr,
      IsReference = nal.RefIdc != 0,
    };

    var codedSliceType = reader.ReadUnsignedExpGolomb();
    if (codedSliceType > 9)
      throw new InvalidDataException(
        $"This H.264 slice states slice_type {codedSliceType}. H.264, Table 7-6 defines 0 to 9 only.");
    header.SliceType = (H264SliceType)(codedSliceType % 5);
    _RefuseSwitchingSlice(header.SliceType);

    var ppsId = reader.ReadUnsignedExpGolomb();
    if (!pictureSets.TryGetValue(ppsId, out var pps))
      throw new InvalidDataException(
        $"This H.264 slice refers to picture parameter set {ppsId}, which has not been seen in this stream.");
    if (!sequenceSets.TryGetValue(pps.SeqParameterSetId, out var sps))
      throw new InvalidDataException(
        $"H.264 picture parameter set {ppsId} refers to sequence parameter set {pps.SeqParameterSetId}, which has not been seen.");

    sps.RefuseUnsupported();
    pps.RefuseUnsupported();
    header.Pps = pps;
    header.Sps = sps;

    if (sps.SeparateColourPlaneFlag)
      reader.Skip(2);
    header.FrameNum = reader.ReadBits(sps.Log2MaxFrameNum);
    if (header.IdrPicFlag)
      header.IdrPicId = reader.ReadUnsignedExpGolomb();

    switch (sps.PicOrderCntType) {
      case 0:
        header.PicOrderCntLsb = reader.ReadBits(sps.Log2MaxPicOrderCntLsb);
        if (pps.BottomFieldPicOrderInFramePresentFlag)
          header.DeltaPicOrderCntBottom = reader.ReadSignedExpGolomb();
        break;
      case 1 when !sps.DeltaPicOrderAlwaysZeroFlag:
        header.DeltaPicOrderCnt0 = reader.ReadSignedExpGolomb();
        if (pps.BottomFieldPicOrderInFramePresentFlag)
          header.DeltaPicOrderCnt1 = reader.ReadSignedExpGolomb();
        break;
    }

    if (pps.RedundantPicCntPresentFlag) {
      header.RedundantPicCnt = reader.ReadUnsignedExpGolomb();
      if (header.RedundantPicCnt > 0)
        throw new NotSupportedException(
          $"This H.264 stream carries redundant_pic_cnt {header.RedundantPicCnt}; redundant coded pictures are not decoded as primaries.");
    }

    if (header.IsB)
      header.DirectSpatialMvPredFlag = reader.ReadBit() != 0;

    header.NumRefIdxL0Active = pps.NumRefIdxL0DefaultActive;
    header.NumRefIdxL1Active = header.IsB ? pps.NumRefIdxL1DefaultActive : 0;
    if (header.SliceType is H264SliceType.P or H264SliceType.B) {
      var overrideActive = reader.ReadBit() != 0;
      if (overrideActive) {
        header.NumRefIdxL0Active = reader.ReadUnsignedExpGolomb() + 1;
        if (header.IsB)
          header.NumRefIdxL1Active = reader.ReadUnsignedExpGolomb() + 1;
      }
    }

    _ValidateReferenceCount(header.NumRefIdxL0Active, 0);
    if (header.IsB)
      _ValidateReferenceCount(header.NumRefIdxL1Active, 1);

    if (!header.IsIntra)
      header.ListModificationsL0 = _ReadListModifications(ref reader, 0);
    if (header.IsB)
      header.ListModificationsL1 = _ReadListModifications(ref reader, 1);

    if (header.SliceType == H264SliceType.P && pps.WeightedPredFlag)
      header.PredictionWeights = H264PredictionWeights.ParseP(ref reader, sps, header.NumRefIdxL0Active);
    else if (header.IsB && pps.WeightedBipredIdc == 1)
      header.PredictionWeights = H264PredictionWeights.ParseB(
        ref reader, sps, header.NumRefIdxL0Active, header.NumRefIdxL1Active);

    if (header.IsReference)
      _ReadReferenceMarking(ref reader, header);

    if (pps.EntropyCodingModeFlag && !header.IsIntra) {
      header.CabacInitIdc = reader.ReadUnsignedExpGolomb();
      if (header.CabacInitIdc > 2)
        throw new InvalidDataException(
          $"This H.264 slice states cabac_init_idc {header.CabacInitIdc}; clause 7.4.3 defines 0 through 2.");
    }

    header.SliceQpY = pps.PicInitQp + reader.ReadSignedExpGolomb();
    if (header.SliceQpY is < 0 or > 51)
      throw new InvalidDataException(
        $"This H.264 slice states SliceQPY {header.SliceQpY}; 8-bit H.264 confines it to 0 through 51.");

    if (pps.DeblockingFilterControlPresentFlag) {
      header.DisableDeblockingFilterIdc = reader.ReadUnsignedExpGolomb();
      if (header.DisableDeblockingFilterIdc > 2)
        throw new InvalidDataException(
          $"This H.264 slice states disable_deblocking_filter_idc {header.DisableDeblockingFilterIdc}; only 0, 1 and 2 exist.");
      if (header.DisableDeblockingFilterIdc != 1) {
        header.FilterOffsetA = reader.ReadSignedExpGolomb() << 1;
        header.FilterOffsetB = reader.ReadSignedExpGolomb() << 1;
      }
    }

    return header;
  }

  private static void _RefuseSwitchingSlice(H264SliceType type) {
    if (type is H264SliceType.SP or H264SliceType.SI)
      throw new NotSupportedException(
        $"This H.264 stream contains an {type} Extended-profile switching slice; switching-slice reconstruction is not implemented.");
  }

  private static void _ValidateReferenceCount(int count, int list) {
    if (count is < 1 or > 32)
      throw new InvalidDataException(
        $"This H.264 slice activates {count} entries of reference picture list {list}; frame pictures permit 1 through 32.");
  }

  private static IReadOnlyList<H264ListModification> _ReadListModifications(ref H264BitReader reader, int list) {
    if (reader.ReadBit() == 0)
      return [];
    var modifications = new List<H264ListModification>();
    while (true) {
      var idc = reader.ReadUnsignedExpGolomb();
      if (idc == 3)
        break;
      if (idc > 3)
        throw new InvalidDataException(
          $"This H.264 list-{list} modification states modification_of_pic_nums_idc {idc}; only 0 through 3 exist.");
      modifications.Add(new(idc, reader.ReadUnsignedExpGolomb()));
      if (modifications.Count > 64)
        throw new InvalidDataException(
          $"An H.264 list-{list} modification exceeded 64 entries without its idc 3 terminator.");
    }
    return modifications;
  }

  private static void _ReadReferenceMarking(ref H264BitReader reader, H264SliceHeader header) {
    if (header.IdrPicFlag) {
      reader.ReadBit(); // no_output_of_prior_pics_flag
      header.LongTermReferenceFlag = reader.ReadBit() != 0;
      return;
    }

    header.AdaptiveRefPicMarkingModeFlag = reader.ReadBit() != 0;
    if (!header.AdaptiveRefPicMarkingModeFlag)
      return;

    var operations = new List<H264MarkingOperation>();
    while (true) {
      var operation = reader.ReadUnsignedExpGolomb();
      if (operation == 0)
        break;
      if (operation > 6)
        throw new InvalidDataException(
          $"This H.264 slice states memory_management_control_operation {operation}; only 0 through 6 exist.");
      var first = operation is 1 or 2 or 3 or 4 ? reader.ReadUnsignedExpGolomb() : 0;
      var second = operation is 3 or 6 ? reader.ReadUnsignedExpGolomb() : 0;
      operations.Add(new(operation, first, second));
      if (operations.Count > 64)
        throw new InvalidDataException("H.264 reference marking exceeded 64 operations without its operation-0 terminator.");
    }
    header.MarkingOperations = operations;
  }
}
