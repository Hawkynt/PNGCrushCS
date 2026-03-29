using System;

namespace FileFormat.Avif.Codec;

/// <summary>AV1 frame types.</summary>
internal enum Av1FrameType {
  Key = 0,
  Inter = 1,
  IntraOnly = 2,
  Switch = 3,
}

/// <summary>AV1 interpolation filter types.</summary>
internal enum Av1InterpolationFilter {
  EightTap = 0,
  EightTapSmooth = 1,
  EightTapSharp = 2,
  Bilinear = 3,
  Switchable = 4,
}

/// <summary>TX mode values.</summary>
internal enum Av1TxMode {
  Only4x4 = 0,
  Largest = 1,
  Select = 2,
}

/// <summary>Parsed AV1 uncompressed frame header (AV1 spec 5.9).</summary>
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
  public int SuperResDenom { get; set; } = 8; // SUPERRES_NUM = 8

  // Render size
  public bool RenderAndFrameSizeDifferent { get; set; }
  public int RenderWidth { get; set; }
  public int RenderHeight { get; set; }

  // Tile info
  public int TileCols { get; set; } = 1;
  public int TileRows { get; set; } = 1;
  public int TileColsLog2 { get; set; }
  public int TileRowsLog2 { get; set; }
  public int[] TileColStarts { get; set; } = [];
  public int[] TileRowStarts { get; set; } = [];
  public int TileSizeBytes { get; set; } = 4;

  // Quantization
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

  // Segmentation
  public bool SegmentationEnabled { get; set; }

  // Delta Q
  public bool DeltaQPresent { get; set; }
  public int DeltaQRes { get; set; }

  // Delta LF
  public bool DeltaLfPresent { get; set; }
  public int DeltaLfRes { get; set; }
  public bool DeltaLfMulti { get; set; }

  // Loop filter
  public int[] LoopFilterLevel { get; set; } = [0, 0, 0, 0];
  public int LoopFilterSharpness { get; set; }
  public bool LoopFilterDeltaEnabled { get; set; }
  public int[] LoopFilterRefDeltas { get; set; } = [1, 0, 0, 0, 0, -1, -1, -1];
  public int[] LoopFilterModeDeltas { get; set; } = [0, 0];

  // CDEF
  public int CdefDamping { get; set; }
  public int CdefBits { get; set; }
  public int[] CdefYPriStrength { get; set; } = [];
  public int[] CdefYSecStrength { get; set; } = [];
  public int[] CdefUvPriStrength { get; set; } = [];
  public int[] CdefUvSecStrength { get; set; } = [];

  // Loop Restoration
  public int[] LrType { get; set; } = [0, 0, 0]; // 0=none, 1=switchable, 2=wiener, 3=sgrproj
  public int[] LrUnitShift { get; set; } = [0, 0, 0];

  // TX mode
  public Av1TxMode TxMode { get; set; }

  // Reference mode
  public bool ReferenceSelect { get; set; }

  // Allow high-precision MV
  public bool AllowHighPrecisionMv { get; set; }

  // Reduced TX set
  public bool ReducedTxSet { get; set; }

  // Allow intra BC
  public bool AllowIntraBc { get; set; }

  // Tile data offset
  public int TileDataOffset { get; set; }

  /// <summary>Parses the uncompressed header for a key frame in an AVIF still image.</summary>
  public static Av1FrameHeader Parse(byte[] data, int offset, int length, Av1SequenceHeader seq) {
    var reader = new Av1BitReader(data, offset, length);
    var fh = new Av1FrameHeader();

    if (seq.ReducedStillPictureHeader) {
      fh.ShowExistingFrame = false;
      fh.FrameType = Av1FrameType.Key;
      fh.ShowFrame = true;
      fh.ShowableFrame = false;
    } else {
      fh.ShowExistingFrame = reader.ReadBool();
      if (fh.ShowExistingFrame)
        throw new NotSupportedException("AV1: show_existing_frame not supported for still images.");

      fh.FrameType = (Av1FrameType)reader.ReadBits(2);
      fh.ShowFrame = reader.ReadBool();
      if (!fh.ShowFrame)
        fh.ShowableFrame = reader.ReadBool();

      if (fh.FrameType != Av1FrameType.Key && fh.FrameType != Av1FrameType.IntraOnly)
        throw new NotSupportedException($"AV1: frame type {fh.FrameType} not supported for AVIF still images.");

      fh.ErrorResilientMode = reader.ReadBool();
    }

    fh.DisableCdfUpdate = reader.ReadBool();

    var allowScreenContentTools = true;
    fh.AllowScreenContentTools = allowScreenContentTools;
    fh.ForceIntegerMv = true;
    fh.AllowIntraBc = false;

    if (fh.FrameType == Av1FrameType.Key) {
      // frame_size()
      _ParseFrameSize(reader, seq, fh);
      // render_size()
      _ParseRenderSize(reader, fh);
      if (fh.AllowScreenContentTools) {
        if (reader.BitsRemaining > 0)
          fh.AllowIntraBc = reader.ReadBool();
      }
    } else {
      _ParseFrameSize(reader, seq, fh);
      _ParseRenderSize(reader, fh);
    }

    // tile_info()
    _ParseTileInfo(reader, seq, fh);

    // quantization_params()
    _ParseQuantizationParams(reader, seq, fh);

    // segmentation_params()
    _ParseSegmentationParams(reader, fh);

    // delta_q_params()
    _ParseDeltaQParams(reader, fh);

    // delta_lf_params()
    _ParseDeltaLfParams(reader, fh);

    // loop_filter_params()
    _ParseLoopFilterParams(reader, seq, fh);

    // cdef_params()
    _ParseCdefParams(reader, seq, fh);

    // lr_params()
    _ParseLrParams(reader, seq, fh);

    // tx_mode
    _ParseTxMode(reader, fh);

    // reference_mode: for key frame always single reference
    fh.ReferenceSelect = false;

    // skip_mode: disabled for key frame

    // Allow warped motion: irrelevant for key frame

    // reduced_tx_set
    fh.ReducedTxSet = reader.ReadBool();

    reader.ByteAlign();
    fh.TileDataOffset = reader.ByteOffset;

    return fh;
  }

  private static void _ParseFrameSize(Av1BitReader reader, Av1SequenceHeader seq, Av1FrameHeader fh) {
    if (seq.EnableSuperRes) {
      fh.UseSuperRes = reader.ReadBool();
      if (fh.UseSuperRes)
        fh.SuperResDenom = (int)reader.ReadBits(3) + 9;
    }

    fh.FrameWidth = seq.MaxFrameWidth;
    fh.FrameHeight = seq.MaxFrameHeight;

    if (fh.UseSuperRes) {
      var upscaledWidth = fh.FrameWidth;
      fh.FrameWidth = (upscaledWidth * 8 + fh.SuperResDenom / 2) / fh.SuperResDenom;
    }
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
    var sbSizeLog2 = seq.Use128x128Superblock ? 7 : 6;
    var miCols = (fh.FrameWidth + 3) / 4; // in 4-pixel MI units
    var miRows = (fh.FrameHeight + 3) / 4;
    var sbCols = (miCols + (sbSize / 4) - 1) / (sbSize / 4);
    var sbRows = (miRows + (sbSize / 4) - 1) / (sbSize / 4);

    var maxTileWidthSb = 4096 / sbSize;
    var maxTileAreaSb = 4096 * 2304 / (sbSize * sbSize);
    var minLog2TileCols = _TileLog2(maxTileWidthSb, sbCols);
    var maxLog2TileCols = _TileLog2(1, Math.Min(sbCols, 64));
    var maxLog2TileRows = _TileLog2(1, Math.Min(sbRows, 64));

    var uniformTileSpacing = reader.ReadBool();
    if (uniformTileSpacing) {
      var tileColsLog2 = minLog2TileCols;
      while (tileColsLog2 < maxLog2TileCols) {
        if (reader.ReadBool())
          ++tileColsLog2;
        else
          break;
      }
      fh.TileColsLog2 = tileColsLog2;

      var tileWidthSb = (sbCols + (1 << tileColsLog2) - 1) >> tileColsLog2;
      var tileCols = 0;
      var colStarts = new int[sbCols + 1];
      for (var startSb = 0; startSb < sbCols; startSb += tileWidthSb)
        colStarts[tileCols++] = startSb;
      colStarts[tileCols] = sbCols;
      fh.TileCols = tileCols;
      fh.TileColStarts = colStarts[..(tileCols + 1)];

      var minLog2TileRows = Math.Max(0, _TileLog2(maxTileAreaSb, sbCols * sbRows / tileCols) - 0);
      var tileRowsLog2 = Math.Max(minLog2TileRows, 0);
      while (tileRowsLog2 < maxLog2TileRows) {
        if (reader.ReadBool())
          ++tileRowsLog2;
        else
          break;
      }
      fh.TileRowsLog2 = tileRowsLog2;

      var tileHeightSb = (sbRows + (1 << tileRowsLog2) - 1) >> tileRowsLog2;
      var tileRows = 0;
      var rowStarts = new int[sbRows + 1];
      for (var startSb = 0; startSb < sbRows; startSb += tileHeightSb)
        rowStarts[tileRows++] = startSb;
      rowStarts[tileRows] = sbRows;
      fh.TileRows = tileRows;
      fh.TileRowStarts = rowStarts[..(tileRows + 1)];
    } else {
      // Non-uniform tile widths
      var widestTileSb = 0;
      var colStarts = new int[sbCols + 1];
      var tileCols = 0;
      var startSb = 0;
      while (startSb < sbCols) {
        colStarts[tileCols] = startSb;
        var maxWidth = Math.Min(sbCols - startSb, maxTileWidthSb);
        var w = (int)reader.ReadNs((uint)maxWidth) + 1;
        if (w > widestTileSb)
          widestTileSb = w;
        startSb += w;
        ++tileCols;
      }
      colStarts[tileCols] = sbCols;
      fh.TileCols = tileCols;
      fh.TileColStarts = colStarts[..(tileCols + 1)];
      fh.TileColsLog2 = _TileLog2(1, tileCols);

      var maxTileHeightSb = Math.Max(1, maxTileAreaSb / widestTileSb);
      var rowStarts2 = new int[sbRows + 1];
      var tileRows = 0;
      startSb = 0;
      while (startSb < sbRows) {
        rowStarts2[tileRows] = startSb;
        var maxHeight = Math.Min(sbRows - startSb, maxTileHeightSb);
        var h = (int)reader.ReadNs((uint)maxHeight) + 1;
        startSb += h;
        ++tileRows;
      }
      rowStarts2[tileRows] = sbRows;
      fh.TileRows = tileRows;
      fh.TileRowStarts = rowStarts2[..(tileRows + 1)];
      fh.TileRowsLog2 = _TileLog2(1, tileRows);
    }

    if (fh.TileCols * fh.TileRows > 1) {
      reader.ReadBits(fh.TileColsLog2 + fh.TileRowsLog2); // context_update_tile_id
      fh.TileSizeBytes = (int)reader.ReadBits(2) + 1;
    }
  }

  private static void _ParseQuantizationParams(Av1BitReader reader, Av1SequenceHeader seq, Av1FrameHeader fh) {
    fh.BaseQIndex = (int)reader.ReadBits(8);
    fh.DeltaQYDc = reader.ReadDeltaQ();

    if (seq.NumPlanes > 1) {
      var diffUvDelta = false;
      if (seq.SeparateUvDeltaQ)
        diffUvDelta = reader.ReadBool();

      fh.DeltaQUDc = reader.ReadDeltaQ();
      fh.DeltaQUAc = reader.ReadDeltaQ();
      if (diffUvDelta) {
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
      if (seq.SeparateUvDeltaQ)
        fh.QmV = (int)reader.ReadBits(4);
      else
        fh.QmV = fh.QmU;
    }
  }

  private static void _ParseSegmentationParams(Av1BitReader reader, Av1FrameHeader fh) {
    fh.SegmentationEnabled = reader.ReadBool();
    if (fh.SegmentationEnabled) {
      // For key frames, segmentation_update_map is always 1
      // Read feature data for all 8 segments x 8 features
      for (var i = 0; i < 8; ++i) {
        for (var j = 0; j < 8; ++j) {
          var featureEnabled = reader.ReadBool();
          if (featureEnabled) {
            var bitsToRead = _SegmentationFeatureBits[j];
            var signed = _SegmentationFeatureSigned[j];
            if (bitsToRead > 0) {
              var value = (int)reader.ReadBits(bitsToRead);
              if (signed && reader.ReadBool())
                value = -value;
            }
          }
        }
      }
    }
  }

  private static readonly int[] _SegmentationFeatureBits = [8, 6, 6, 6, 6, 3, 0, 0];
  private static readonly bool[] _SegmentationFeatureSigned = [true, true, true, true, true, false, false, false];

  private static void _ParseDeltaQParams(Av1BitReader reader, Av1FrameHeader fh) {
    fh.DeltaQPresent = false;
    if (fh.BaseQIndex > 0)
      fh.DeltaQPresent = reader.ReadBool();
    if (fh.DeltaQPresent)
      fh.DeltaQRes = (int)reader.ReadBits(2);
  }

  private static void _ParseDeltaLfParams(Av1BitReader reader, Av1FrameHeader fh) {
    fh.DeltaLfPresent = false;
    if (fh.DeltaQPresent) {
      fh.DeltaLfPresent = reader.ReadBool();
      if (fh.DeltaLfPresent) {
        fh.DeltaLfRes = (int)reader.ReadBits(2);
        fh.DeltaLfMulti = reader.ReadBool();
      }
    }
  }

  private static void _ParseLoopFilterParams(Av1BitReader reader, Av1SequenceHeader seq, Av1FrameHeader fh) {
    if (fh.AllowIntraBc) {
      // Loop filter is off for IntraBC frames
      return;
    }

    fh.LoopFilterLevel[0] = (int)reader.ReadBits(6);
    fh.LoopFilterLevel[1] = (int)reader.ReadBits(6);
    if (seq.NumPlanes > 1 && (fh.LoopFilterLevel[0] != 0 || fh.LoopFilterLevel[1] != 0)) {
      fh.LoopFilterLevel[2] = (int)reader.ReadBits(6);
      fh.LoopFilterLevel[3] = (int)reader.ReadBits(6);
    }
    fh.LoopFilterSharpness = (int)reader.ReadBits(3);

    fh.LoopFilterDeltaEnabled = reader.ReadBool();
    if (fh.LoopFilterDeltaEnabled) {
      var loopFilterDeltaUpdate = reader.ReadBool();
      if (loopFilterDeltaUpdate) {
        for (var i = 0; i < 8; ++i) {
          if (reader.ReadBool())
            fh.LoopFilterRefDeltas[i] = reader.ReadSu(7);
        }
        for (var i = 0; i < 2; ++i) {
          if (reader.ReadBool())
            fh.LoopFilterModeDeltas[i] = reader.ReadSu(7);
        }
      }
    }
  }

  private static void _ParseCdefParams(Av1BitReader reader, Av1SequenceHeader seq, Av1FrameHeader fh) {
    if (!seq.EnableCdef || fh.AllowIntraBc) {
      fh.CdefBits = 0;
      fh.CdefDamping = 3;
      fh.CdefYPriStrength = [0];
      fh.CdefYSecStrength = [0];
      fh.CdefUvPriStrength = [0];
      fh.CdefUvSecStrength = [0];
      return;
    }

    fh.CdefDamping = (int)reader.ReadBits(2) + 3;
    fh.CdefBits = (int)reader.ReadBits(2);
    var numCdefStrengths = 1 << fh.CdefBits;

    fh.CdefYPriStrength = new int[numCdefStrengths];
    fh.CdefYSecStrength = new int[numCdefStrengths];
    fh.CdefUvPriStrength = new int[numCdefStrengths];
    fh.CdefUvSecStrength = new int[numCdefStrengths];

    for (var i = 0; i < numCdefStrengths; ++i) {
      fh.CdefYPriStrength[i] = (int)reader.ReadBits(4);
      fh.CdefYSecStrength[i] = (int)reader.ReadBits(2);
      if (fh.CdefYSecStrength[i] == 3)
        ++fh.CdefYSecStrength[i];

      if (seq.NumPlanes > 1) {
        fh.CdefUvPriStrength[i] = (int)reader.ReadBits(4);
        fh.CdefUvSecStrength[i] = (int)reader.ReadBits(2);
        if (fh.CdefUvSecStrength[i] == 3)
          ++fh.CdefUvSecStrength[i];
      }
    }
  }

  private static void _ParseLrParams(Av1BitReader reader, Av1SequenceHeader seq, Av1FrameHeader fh) {
    if (!seq.EnableRestoration || fh.AllowIntraBc) {
      fh.LrType = [0, 0, 0];
      return;
    }

    var usesLr = false;
    var usesChromaLr = false;
    for (var i = 0; i < seq.NumPlanes; ++i) {
      fh.LrType[i] = (int)reader.ReadBits(2);
      if (fh.LrType[i] != 0) {
        usesLr = true;
        if (i > 0)
          usesChromaLr = true;
      }
    }

    if (usesLr) {
      if (seq.Use128x128Superblock) {
        fh.LrUnitShift[0] = (int)reader.ReadBits(1) + 1;
      } else {
        fh.LrUnitShift[0] = (int)reader.ReadBits(1);
        if (fh.LrUnitShift[0] != 0) {
          var additionalShift = (int)reader.ReadBits(1);
          fh.LrUnitShift[0] += additionalShift;
        }
      }

      fh.LrUnitShift[1] = fh.LrUnitShift[0];
      fh.LrUnitShift[2] = fh.LrUnitShift[0];

      if (seq.SubsamplingX != 0 && seq.SubsamplingY != 0 && usesChromaLr) {
        var lrUvShift = reader.ReadBool() ? 1 : 0;
        fh.LrUnitShift[1] = fh.LrUnitShift[0] - lrUvShift;
        fh.LrUnitShift[2] = fh.LrUnitShift[0] - lrUvShift;
      }
    }
  }

  private static void _ParseTxMode(Av1BitReader reader, Av1FrameHeader fh) {
    if (fh.BaseQIndex == 0) {
      fh.TxMode = Av1TxMode.Only4x4;
    } else {
      var txModeSelect = reader.ReadBool();
      fh.TxMode = txModeSelect ? Av1TxMode.Select : Av1TxMode.Largest;
    }
  }

  private static int _TileLog2(int blkSize, int target) {
    var k = 0;
    while ((blkSize << k) < target)
      ++k;
    return k;
  }
}
