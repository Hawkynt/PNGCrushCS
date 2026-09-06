using System;

namespace FileFormat.Avif.Codec;

internal enum Av1FrameType { Key = 0, Inter = 1, IntraOnly = 2, Switch = 3 }
internal enum Av1InterpolationFilter { EightTap = 0, EightTapSmooth = 1, EightTapSharp = 2, Bilinear = 3, Switchable = 4 }
internal enum Av1TxMode { Only4x4 = 0, Largest = 1, Select = 2 }

/// <summary>Parsed AV1 uncompressed frame header (AV1 specification 5.9).</summary>
internal sealed class Av1FrameHeader {
  public Av1FrameType FrameType { get; set; }
  public bool ShowExistingFrame { get; set; }
  public bool ShowFrame { get; set; }
  public bool ShowableFrame { get; set; }
  public bool ErrorResilientMode { get; set; }
  public bool DisableCdfUpdate { get; set; }
  public bool AllowScreenContentTools { get; set; }
  public bool ForceIntegerMv { get; set; }
  public int FrameWidth { get; set; }
  public int FrameHeight { get; set; }
  public bool UseSuperRes { get; set; }
  public int SuperResDenom { get; set; } = 8;
  public bool RenderAndFrameSizeDifferent { get; set; }
  public int RenderWidth { get; set; }
  public int RenderHeight { get; set; }
  public int TileCols { get; set; } = 1;
  public int TileRows { get; set; } = 1;
  public int TileColsLog2 { get; set; }
  public int TileRowsLog2 { get; set; }
  public int[] TileColStarts { get; set; } = [];
  public int[] TileRowStarts { get; set; } = [];
  public int TileSizeBytes { get; set; } = 4;
  public int BaseQIndex { get; set; }
  public int DeltaQYDc { get; set; }
  public bool UsingQMatrix { get; set; }
  public int QmY { get; set; }
  public int QmU { get; set; }
  public int QmV { get; set; }
  public int DeltaQUDc { get; set; }
  public int DeltaQUAc { get; set; }
  public int DeltaQVDc { get; set; }
  public int DeltaQVAc { get; set; }
  public bool SegmentationEnabled { get; set; }
  public bool DeltaQPresent { get; set; }
  public int DeltaQRes { get; set; }
  public bool DeltaLfPresent { get; set; }
  public int DeltaLfRes { get; set; }
  public bool DeltaLfMulti { get; set; }
  public int[] LoopFilterLevel { get; set; } = [0, 0, 0, 0];
  public int LoopFilterSharpness { get; set; }
  public bool LoopFilterDeltaEnabled { get; set; }
  public int[] LoopFilterRefDeltas { get; set; } = [1, 0, 0, 0, 0, -1, -1, -1];
  public int[] LoopFilterModeDeltas { get; set; } = [0, 0];
  public int CdefDamping { get; set; }
  public int CdefBits { get; set; }
  public int[] CdefYPriStrength { get; set; } = [];
  public int[] CdefYSecStrength { get; set; } = [];
  public int[] CdefUvPriStrength { get; set; } = [];
  public int[] CdefUvSecStrength { get; set; } = [];
  public int[] LrType { get; set; } = [0, 0, 0];
  public int[] LrUnitShift { get; set; } = [0, 0, 0];

  /// <summary>Loop-restoration unit size per plane, in that plane's samples (AV1 5.9.20).</summary>
  public int[] LoopRestorationSize { get; set; } = [0, 0, 0];

  /// <summary>Whether every coded block in the frame is lossless (AV1 5.9.2 CodedLossless).</summary>
  public bool CodedLossless { get; set; }

  /// <summary>CodedLossless without super-resolution in play (AV1 5.9.2 AllLossless).</summary>
  public bool AllLossless { get; set; }

  /// <summary>Frame width before any super-resolution downscale.</summary>
  public int UpscaledWidth { get; set; }
  public Av1TxMode TxMode { get; set; }
  public bool ReferenceSelect { get; set; }
  public bool AllowHighPrecisionMv { get; set; }
  public bool ReducedTxSet { get; set; }
  public bool AllowIntraBc { get; set; }
  public int TileDataOffset { get; set; }

  public static Av1FrameHeader Parse(byte[] data, int offset, int length, Av1SequenceHeader seq) {
    var reader = new Av1BitReader(data, offset, length);
    var fh = new Av1FrameHeader();

    if (seq.ReducedStillPictureHeader) {
      fh.ShowExistingFrame = false;
      fh.FrameType = Av1FrameType.Key;
      fh.ShowFrame = true;
      fh.ShowableFrame = false;
      fh.ErrorResilientMode = true;
    } else {
      fh.ShowExistingFrame = reader.ReadBool();
      if (fh.ShowExistingFrame)
        throw new NotSupportedException("AV1: show_existing_frame is not supported for still images.");

      fh.FrameType = (Av1FrameType)reader.ReadBits(2);
      if (fh.FrameType != Av1FrameType.Key && fh.FrameType != Av1FrameType.IntraOnly)
        throw new NotSupportedException($"AV1: frame type {fh.FrameType} is not supported for AVIF still images.");

      fh.ShowFrame = reader.ReadBool();
      if (fh.ShowFrame && seq.DecoderModelInfoPresent && !seq.EqualPictureInterval)
        reader.ReadBits(seq.FramePresentationTimeLength); // temporal_point_info()

      if (fh.ShowFrame)
        fh.ShowableFrame = fh.FrameType != Av1FrameType.Key;
      else
        fh.ShowableFrame = reader.ReadBool();

      fh.ErrorResilientMode = fh.FrameType == Av1FrameType.Switch
        || (fh.FrameType == Av1FrameType.Key && fh.ShowFrame)
        || reader.ReadBool();
    }

    fh.DisableCdfUpdate = reader.ReadBool();

    fh.AllowScreenContentTools = seq.SeqForceScreenContentTools == 2
      ? reader.ReadBool()
      : seq.SeqForceScreenContentTools != 0;
    if (fh.AllowScreenContentTools)
      fh.ForceIntegerMv = seq.SeqForceIntegerMv == 2 ? reader.ReadBool() : seq.SeqForceIntegerMv != 0;
    else
      fh.ForceIntegerMv = fh.FrameType is Av1FrameType.Key or Av1FrameType.IntraOnly;

    if (seq.FrameIdNumbersPresent)
      throw new NotSupportedException("AV1: frame IDs are not supported by this still-image decoder.");

    // The fields between the screen-content flags and the frame size are all absent from a reduced
    // still-picture header, but an encoder is free to write a full one for a single image and
    // several do, so they are parsed rather than assumed away.
    var frameSizeOverride = false;
    if (!seq.ReducedStillPictureHeader) {
      frameSizeOverride = fh.FrameType == Av1FrameType.Switch || reader.ReadBool();
      reader.ReadBits(seq.OrderHintBits); // order_hint

      // primary_ref_frame is PRIMARY_REF_NONE for every frame this decoder accepts.
      if (seq.DecoderModelInfoPresent && reader.ReadBool())
        for (var i = 0; i < seq.OperatingPointsCount; ++i)
          if (seq.DecoderModelPresentForOperatingPoint[i])
            reader.ReadBits(seq.BufferRemovalTimeLength);

      var refreshesAll = fh.FrameType == Av1FrameType.Switch
        || (fh.FrameType == Av1FrameType.Key && fh.ShowFrame);
      var refreshFrameFlags = refreshesAll ? 0xFF : (int)reader.ReadBits(8);
      if (refreshFrameFlags != 0xFF && fh.ErrorResilientMode && seq.EnableOrderHint)
        for (var i = 0; i < 8; ++i)
          reader.ReadBits(seq.OrderHintBits);
    }

    _ParseFrameSize(reader, seq, fh, frameSizeOverride);
    _ParseRenderSize(reader, fh);
    if (fh.AllowScreenContentTools && fh.UpscaledWidth == fh.FrameWidth)
      fh.AllowIntraBc = reader.ReadBool();

    if (!seq.ReducedStillPictureHeader && !fh.DisableCdfUpdate)
      reader.ReadBool(); // disable_frame_end_update_cdf

    _ParseTileInfo(reader, seq, fh);
    _ParseQuantizationParams(reader, seq, fh);
    _ParseSegmentationParams(reader, fh);
    _ParseDeltaQParams(reader, fh);
    _ParseDeltaLfParams(reader, fh);

    // AV1 5.9.2: whether the frame is lossless decides whether the loop filter, CDEF and loop
    // restoration are signalled at all, so it has to be settled before those are parsed.
    fh.CodedLossless = fh.BaseQIndex == 0
      && fh.DeltaQYDc == 0
      && fh.DeltaQUAc == 0 && fh.DeltaQUDc == 0
      && fh.DeltaQVAc == 0 && fh.DeltaQVDc == 0;
    fh.AllLossless = fh.CodedLossless && fh.FrameWidth == fh.UpscaledWidth;

    _ParseLoopFilterParams(reader, seq, fh);
    _ParseCdefParams(reader, seq, fh);
    _ParseLrParams(reader, seq, fh);
    _ParseTxMode(reader, fh);
    fh.ReferenceSelect = false;
    fh.ReducedTxSet = reader.ReadBool();
    reader.ByteAlign();

    // Relative to the OBU payload, because that is what the caller has a pointer to.
    fh.TileDataOffset = reader.ByteOffset - offset;
    return fh;
  }

  private static void _ParseFrameSize(Av1BitReader reader, Av1SequenceHeader seq, Av1FrameHeader fh, bool sizeOverride) {
    if (sizeOverride) {
      fh.FrameWidth = (int)reader.ReadBits(seq.FrameWidthBits) + 1;
      fh.FrameHeight = (int)reader.ReadBits(seq.FrameHeightBits) + 1;
    } else {
      fh.FrameWidth = seq.MaxFrameWidth;
      fh.FrameHeight = seq.MaxFrameHeight;
    }

    if (seq.EnableSuperRes) {
      fh.UseSuperRes = reader.ReadBool();
      if (fh.UseSuperRes)
        fh.SuperResDenom = (int)reader.ReadBits(3) + 9;
    }

    fh.UpscaledWidth = fh.FrameWidth;
    if (fh.UseSuperRes)
      throw new NotSupportedException("AV1: super-resolution is not supported by this still-image decoder.");
  }

  private static void _ParseRenderSize(Av1BitReader reader, Av1FrameHeader fh) {
    fh.RenderAndFrameSizeDifferent = reader.ReadBool();
    if (fh.RenderAndFrameSizeDifferent) {
      fh.RenderWidth = (int)reader.ReadBits(16) + 1;
      fh.RenderHeight = (int)reader.ReadBits(16) + 1;
    } else {
      fh.RenderWidth = fh.FrameWidth;
      fh.RenderHeight = fh.FrameHeight;
    }
  }

  private static void _ParseTileInfo(Av1BitReader reader, Av1SequenceHeader seq, Av1FrameHeader fh) {
    var sbSize = seq.Use128x128Superblock ? 128 : 64;
    var miCols = (fh.FrameWidth + 3) / 4;
    var miRows = (fh.FrameHeight + 3) / 4;
    var sbCols = (miCols + sbSize / 4 - 1) / (sbSize / 4);
    var sbRows = (miRows + sbSize / 4 - 1) / (sbSize / 4);
    var maxTileWidthSb = 4096 / sbSize;
    var maxTileAreaSb = 4096 * 2304 / (sbSize * sbSize);
    var minLog2TileCols = _TileLog2(maxTileWidthSb, sbCols);
    var maxLog2TileCols = _TileLog2(1, Math.Min(sbCols, Av1Constants.MaxTileCols));
    var maxLog2TileRows = _TileLog2(1, Math.Min(sbRows, Av1Constants.MaxTileRows));
    var minLog2Tiles = Math.Max(minLog2TileCols, _TileLog2(maxTileAreaSb, sbRows * sbCols));

    if (reader.ReadBool()) {
      var tileColsLog2 = minLog2TileCols;
      while (tileColsLog2 < maxLog2TileCols && reader.ReadBool())
        ++tileColsLog2;
      fh.TileColsLog2 = tileColsLog2;
      var tileWidthSb = (sbCols + (1 << tileColsLog2) - 1) >> tileColsLog2;
      var cols = new int[sbCols + 1];
      var ncols = 0;
      for (var start = 0; start < sbCols; start += tileWidthSb)
        cols[ncols++] = start;
      cols[ncols] = sbCols;
      fh.TileCols = ncols;
      fh.TileColStarts = cols[..(ncols + 1)];

      var minLog2TileRows = Math.Max(minLog2Tiles - tileColsLog2, 0);
      var tileRowsLog2 = minLog2TileRows;
      while (tileRowsLog2 < maxLog2TileRows && reader.ReadBool())
        ++tileRowsLog2;
      fh.TileRowsLog2 = tileRowsLog2;
      var tileHeightSb = (sbRows + (1 << tileRowsLog2) - 1) >> tileRowsLog2;
      var rows = new int[sbRows + 1];
      var nrows = 0;
      for (var start = 0; start < sbRows; start += tileHeightSb)
        rows[nrows++] = start;
      rows[nrows] = sbRows;
      fh.TileRows = nrows;
      fh.TileRowStarts = rows[..(nrows + 1)];
    } else {
      var widest = 0;
      var cols = new int[sbCols + 1];
      var ncols = 0;
      var start = 0;
      while (start < sbCols) {
        cols[ncols] = start;
        var maxWidth = Math.Min(sbCols - start, maxTileWidthSb);
        var width = (int)reader.ReadNs((uint)maxWidth) + 1;
        widest = Math.Max(widest, width);
        start += width;
        ++ncols;
      }
      cols[ncols] = sbCols;
      fh.TileCols = ncols;
      fh.TileColStarts = cols[..(ncols + 1)];
      fh.TileColsLog2 = _TileLog2(1, ncols);

      // AV1 5.9.15: once the column layout is known the area budget is recomputed from it.
      var rowAreaSb = minLog2Tiles > 0 ? (sbRows * sbCols) >> (minLog2Tiles + 1) : sbRows * sbCols;
      var maxHeightSb = Math.Max(1, rowAreaSb / widest);
      var rows = new int[sbRows + 1];
      var nrows = 0;
      start = 0;
      while (start < sbRows) {
        rows[nrows] = start;
        var maxHeight = Math.Min(sbRows - start, maxHeightSb);
        start += (int)reader.ReadNs((uint)maxHeight) + 1;
        ++nrows;
      }
      rows[nrows] = sbRows;
      fh.TileRows = nrows;
      fh.TileRowStarts = rows[..(nrows + 1)];
      fh.TileRowsLog2 = _TileLog2(1, nrows);
    }

    if (fh.TileCols * fh.TileRows > 1) {
      reader.ReadBits(fh.TileColsLog2 + fh.TileRowsLog2);
      fh.TileSizeBytes = (int)reader.ReadBits(2) + 1;
    }
  }

  private static void _ParseQuantizationParams(Av1BitReader reader, Av1SequenceHeader seq, Av1FrameHeader fh) {
    fh.BaseQIndex = (int)reader.ReadBits(8);
    fh.DeltaQYDc = reader.ReadDeltaQ();
    if (seq.NumPlanes > 1) {
      var diffUv = seq.SeparateUvDeltaQ && reader.ReadBool();
      fh.DeltaQUDc = reader.ReadDeltaQ();
      fh.DeltaQUAc = reader.ReadDeltaQ();
      if (diffUv) {
        fh.DeltaQVDc = reader.ReadDeltaQ();
        fh.DeltaQVAc = reader.ReadDeltaQ();
      } else {
        fh.DeltaQVDc = fh.DeltaQUDc;
        fh.DeltaQVAc = fh.DeltaQUAc;
      }
    }
    fh.UsingQMatrix = reader.ReadBool();
    if (fh.UsingQMatrix) {
      fh.QmY = (int)reader.ReadBits(4);
      fh.QmU = (int)reader.ReadBits(4);
      fh.QmV = seq.SeparateUvDeltaQ ? (int)reader.ReadBits(4) : fh.QmU;
    }
  }

  private static void _ParseSegmentationParams(Av1BitReader reader, Av1FrameHeader fh) {
    fh.SegmentationEnabled = reader.ReadBool();
    if (!fh.SegmentationEnabled)
      return;
    int[] bits = [8, 6, 6, 6, 6, 3, 0, 0];
    bool[] signed = [true, true, true, true, true, false, false, false];
    for (var i = 0; i < 8; ++i)
    for (var j = 0; j < 8; ++j)
      if (reader.ReadBool() && bits[j] > 0) {
        reader.ReadBits(bits[j]);
        if (signed[j]) reader.ReadBool();
      }
  }

  private static void _ParseDeltaQParams(Av1BitReader reader, Av1FrameHeader fh) {
    if (fh.BaseQIndex <= 0)
      return;
    fh.DeltaQPresent = reader.ReadBool();
    if (fh.DeltaQPresent)
      fh.DeltaQRes = (int)reader.ReadBits(2);
  }

  private static void _ParseDeltaLfParams(Av1BitReader reader, Av1FrameHeader fh) {
    if (!fh.DeltaQPresent)
      return;
    fh.DeltaLfPresent = reader.ReadBool();
    if (fh.DeltaLfPresent) {
      fh.DeltaLfRes = (int)reader.ReadBits(2);
      fh.DeltaLfMulti = reader.ReadBool();
    }
  }

  private static void _ParseLoopFilterParams(Av1BitReader reader, Av1SequenceHeader seq, Av1FrameHeader fh) {
    if (fh.CodedLossless || fh.AllowIntraBc)
      return;
    fh.LoopFilterLevel[0] = (int)reader.ReadBits(6);
    fh.LoopFilterLevel[1] = (int)reader.ReadBits(6);
    if (seq.NumPlanes > 1 && (fh.LoopFilterLevel[0] != 0 || fh.LoopFilterLevel[1] != 0)) {
      fh.LoopFilterLevel[2] = (int)reader.ReadBits(6);
      fh.LoopFilterLevel[3] = (int)reader.ReadBits(6);
    }
    fh.LoopFilterSharpness = (int)reader.ReadBits(3);
    fh.LoopFilterDeltaEnabled = reader.ReadBool();
    if (!fh.LoopFilterDeltaEnabled || !reader.ReadBool())
      return;
    for (var i = 0; i < 8; ++i)
      if (reader.ReadBool()) fh.LoopFilterRefDeltas[i] = reader.ReadSu(7);
    for (var i = 0; i < 2; ++i)
      if (reader.ReadBool()) fh.LoopFilterModeDeltas[i] = reader.ReadSu(7);
  }

  private static void _ParseCdefParams(Av1BitReader reader, Av1SequenceHeader seq, Av1FrameHeader fh) {
    if (fh.CodedLossless || fh.AllowIntraBc || !seq.EnableCdef) {
      fh.CdefBits = 0;
      fh.CdefDamping = 3;
      fh.CdefYPriStrength = fh.CdefYSecStrength = fh.CdefUvPriStrength = fh.CdefUvSecStrength = [0];
      return;
    }
    fh.CdefDamping = (int)reader.ReadBits(2) + 3;
    fh.CdefBits = (int)reader.ReadBits(2);
    var n = 1 << fh.CdefBits;
    fh.CdefYPriStrength = new int[n];
    fh.CdefYSecStrength = new int[n];
    fh.CdefUvPriStrength = new int[n];
    fh.CdefUvSecStrength = new int[n];
    for (var i = 0; i < n; ++i) {
      fh.CdefYPriStrength[i] = (int)reader.ReadBits(4);
      fh.CdefYSecStrength[i] = (int)reader.ReadBits(2);
      if (fh.CdefYSecStrength[i] == 3) ++fh.CdefYSecStrength[i];
      if (seq.NumPlanes <= 1) continue;
      fh.CdefUvPriStrength[i] = (int)reader.ReadBits(4);
      fh.CdefUvSecStrength[i] = (int)reader.ReadBits(2);
      if (fh.CdefUvSecStrength[i] == 3) ++fh.CdefUvSecStrength[i];
    }
  }

  private static void _ParseLrParams(Av1BitReader reader, Av1SequenceHeader seq, Av1FrameHeader fh) {
    if (fh.AllLossless || fh.AllowIntraBc || !seq.EnableRestoration)
      return;

    var uses = false;
    var usesChroma = false;
    for (var i = 0; i < seq.NumPlanes; ++i) {
      // lr_type is coded in signalling order; Remap_Lr_Type turns it into the restoration type,
      // which happens to be the same order as Av1RestorationType.
      fh.LrType[i] = (int)reader.ReadBits(2);
      uses |= fh.LrType[i] != 0;
      usesChroma |= i > 0 && fh.LrType[i] != 0;
    }

    if (!uses)
      return;

    int unitShift;
    if (seq.Use128x128Superblock)
      unitShift = (int)reader.ReadBits(1) + 1;
    else {
      unitShift = (int)reader.ReadBits(1);
      if (unitShift != 0)
        unitShift += (int)reader.ReadBits(1);
    }

    fh.LrUnitShift[0] = unitShift;
    fh.LoopRestorationSize[0] = Av1Constants.RestorationTileSizeMax >> (2 - unitShift);

    var uvShift = seq.SubsamplingX != 0 && seq.SubsamplingY != 0 && usesChroma && reader.ReadBool() ? 1 : 0;
    fh.LrUnitShift[1] = fh.LrUnitShift[2] = unitShift - uvShift;
    fh.LoopRestorationSize[1] = fh.LoopRestorationSize[0] >> uvShift;
    fh.LoopRestorationSize[2] = fh.LoopRestorationSize[0] >> uvShift;
  }

  private static void _ParseTxMode(Av1BitReader reader, Av1FrameHeader fh) {
    if (fh.CodedLossless) {
      fh.TxMode = Av1TxMode.Only4x4;
      return;
    }
    fh.TxMode = reader.ReadBool() ? Av1TxMode.Select : Av1TxMode.Largest;
  }

  private static int _TileLog2(int blockSize, int target) {
    var k = 0;
    while ((blockSize << k) < target) ++k;
    return k;
  }
}
