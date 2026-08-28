using System;
using System.IO;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9;

/// <summary>
/// The uncompressed header of a VP9 frame, and the parts of it that outlive the frame
/// (specification 6.2 and 7.2).
/// </summary>
/// <remarks>
/// One of these lives for the whole stream rather than for one frame, because a good deal of what a
/// frame header says is what it does <em>not</em> say. Loop filter deltas, segment features, the
/// segmentation map and the reference sign biases all persist from frame to frame until a header
/// changes them or a frame asks to be independent of its past. A header parsed into a fresh object
/// each time would silently reset all of them and produce a picture that is wrong only where the
/// stream was economical.
/// <para/>
/// Profiles 0 and 1 are eight-bit and share the same entropy, transform and quantisation machinery.
/// Profile 1 adds independently stated chroma subsampling (and permits sRGB); profiles 2 and 3 raise
/// the sample depth to ten or twelve bits and are still refused before any picture is reconstructed.
/// </remarks>
internal sealed class Vp9FrameHeader {

  private const int FRAME_MARKER = 2;
  private const int SYNC_BYTE_0 = 0x49;
  private const int SYNC_BYTE_1 = 0x83;
  private const int SYNC_BYTE_2 = 0x42;

  // --------------------------------------------------------------------------------------------
  // Per-frame
  // --------------------------------------------------------------------------------------------

  internal int Profile;
  internal bool ShowExistingFrame;
  internal int FrameToShowMapIndex;
  internal int FrameType;
  internal int LastFrameType;
  internal bool ShowFrame;
  internal bool ErrorResilientMode;
  internal bool IntraOnly;
  internal bool FrameIsIntra;
  internal int ResetFrameContext;
  internal int RefreshFrameFlags;
  internal readonly int[] ReferenceFrameIndex = new int[REFS_PER_FRAME];
  internal bool AllowHighPrecisionMotionVectors;
  internal int InterpolationFilter;
  internal bool RefreshFrameContext;
  internal bool FrameParallelDecodingMode;
  internal int FrameContextIndex;
  internal int HeaderSizeInBytes;

  internal int FrameWidth;
  internal int FrameHeight;
  internal int RenderWidth;
  internal int RenderHeight;
  internal int MiCols;
  internal int MiRows;
  internal int Sb64Cols;
  internal int Sb64Rows;
  internal bool UsePreviousFrameMotionVectors;

  internal int ColorSpace;
  internal int ColorRange;
  internal int SubsamplingX = 1;
  internal int SubsamplingY = 1;

  internal int LoopFilterLevel;
  internal int LoopFilterSharpness;
  internal bool LoopFilterDeltaEnabled;
  internal bool LoopFilterDeltaUpdate;

  internal int BaseQIndex;
  internal int DeltaQYDc;
  internal int DeltaQUvDc;
  internal int DeltaQUvAc;
  internal bool Lossless;

  internal bool SegmentationEnabled;
  internal bool SegmentationUpdateMap;
  internal bool SegmentationTemporalUpdate;

  internal int TileColsLog2;
  internal int TileRowsLog2;

  // Read from the compressed header, but every reader of them thinks of them as frame settings.
  internal int TransformMode;
  internal int ReferenceMode;
  internal int CompoundFixedReference;
  internal readonly int[] CompoundVariableReference = new int[2];

  // --------------------------------------------------------------------------------------------
  // Carried from frame to frame
  // --------------------------------------------------------------------------------------------

  /// <summary>Which direction in time each reference frame lies, indexed by reference frame.</summary>
  internal readonly int[] ReferenceFrameSignBias = new int[MAX_REF_FRAMES];

  internal readonly int[] LoopFilterReferenceDeltas = new int[MAX_REF_FRAMES];
  internal readonly int[] LoopFilterModeDeltas = new int[MAX_MODE_LF_DELTAS];

  internal bool SegmentationAbsoluteValues;
  internal readonly byte[] SegmentationTreeProbabilities = new byte[7];
  internal readonly byte[] SegmentationPredictionProbabilities = new byte[3];
  internal readonly bool[] FeatureEnabled = new bool[MAX_SEGMENTS * SEG_LVL_MAX];
  internal readonly int[] FeatureData = new int[MAX_SEGMENTS * SEG_LVL_MAX];

  private bool _haveComputedImageSize;
  private int _previousWidth;
  private int _previousHeight;
  private bool _previousShowFrame;

  /// <summary>Whether the picture size changed, which is when the caller has to rebuild its buffers.</summary>
  internal bool SizeChanged { get; private set; }

  internal bool IsFeatureActive(int segment, int feature)
    => this.SegmentationEnabled && this.FeatureEnabled[segment * SEG_LVL_MAX + feature];

  internal int Feature(int segment, int feature) => this.FeatureData[segment * SEG_LVL_MAX + feature];

  /// <summary>
  /// Reads one uncompressed header (specification 6.2).
  /// </summary>
  /// <param name="reader">Positioned at the first byte of the frame.</param>
  /// <param name="referenceWidths">The width of each of the eight reference slots, for a frame that takes its size from one.</param>
  /// <param name="referenceHeights">The height of each of the eight reference slots.</param>
  /// <param name="slotIsValid">Which of the eight reference slots have ever been written.</param>
  internal void Parse(ref Vp9BitReader reader, int[] referenceWidths, int[] referenceHeights, bool[] slotIsValid) {
    this.SizeChanged = false;

    if (reader.ReadLiteral(2) != FRAME_MARKER)
      throw new InvalidDataException(
        "This VP9 frame does not begin with the two-bit frame marker of 2 that specification 7.2 requires. Either "
        + "the packet is not VP9 or the frame boundaries were computed wrongly.");

    var profileLowBit = reader.ReadBit();
    var profileHighBit = reader.ReadBit();
    this.Profile = (profileHighBit << 1) + profileLowBit;

    if (this.Profile >= 2)
      throw new NotSupportedException(
        $"This VP9 stream states profile {this.Profile}. Profiles 2 and 3 code ten- or twelve-bit samples; this "
        + "decoder currently reconstructs the complete eight-bit profile 0/1 pipeline. High-bit-depth prediction, "
        + "inverse transforms, dequantisation and loop filtering are not implemented yet.");

    this.ShowExistingFrame = reader.ReadBit() != 0;
    if (this.ShowExistingFrame) {
      this.FrameToShowMapIndex = reader.ReadLiteral(3);
      this.HeaderSizeInBytes = 0;
      this.RefreshFrameFlags = 0;
      this.LoopFilterLevel = 0;
      return;
    }

    this.LastFrameType = this.FrameType;
    this.FrameType = reader.ReadBit();
    this.ShowFrame = reader.ReadBit() != 0;
    this.ErrorResilientMode = reader.ReadBit() != 0;

    if (this.FrameType == KEY_FRAME) {
      _ReadSyncCode(ref reader);
      this._ReadColorConfig(ref reader);

      this.IntraOnly = false;
      this.FrameIsIntra = true;

      this._ReadFrameSize(ref reader);
      this._ReadRenderSize(ref reader);
      this.RefreshFrameFlags = 0xFF;
      this.ResetFrameContext = 0;
    } else {
      this.IntraOnly = !this.ShowFrame && reader.ReadBit() != 0;
      this.FrameIsIntra = this.IntraOnly;
      this.ResetFrameContext = this.ErrorResilientMode ? 0 : reader.ReadLiteral(2);

      if (this.IntraOnly) {
        _ReadSyncCode(ref reader);

        if (this.Profile > 0)
          this._ReadColorConfig(ref reader);
        else {
          this.ColorSpace = CS_BT_601;
          this.ColorRange = 0;
          this.SubsamplingX = 1;
          this.SubsamplingY = 1;
        }

        this.RefreshFrameFlags = reader.ReadLiteral(8);
        this._ReadFrameSize(ref reader);
        this._ReadRenderSize(ref reader);
      } else {
        this.RefreshFrameFlags = reader.ReadLiteral(8);
        for (var i = 0; i < REFS_PER_FRAME; ++i) {
          this.ReferenceFrameIndex[i] = reader.ReadLiteral(3);
          this.ReferenceFrameSignBias[LAST_FRAME + i] = reader.ReadBit();
        }

        this._ReadFrameSizeWithReferences(ref reader, referenceWidths, referenceHeights, slotIsValid);
        this.AllowHighPrecisionMotionVectors = reader.ReadBit() != 0;
        this._ReadInterpolationFilter(ref reader);
      }
    }

    if (this.ErrorResilientMode) {
      this.RefreshFrameContext = false;
      this.FrameParallelDecodingMode = true;
    } else {
      this.RefreshFrameContext = reader.ReadBit() != 0;
      this.FrameParallelDecodingMode = reader.ReadBit() != 0;
    }

    this.FrameContextIndex = reader.ReadLiteral(2);

    this.NeedsPastIndependence = this.FrameIsIntra || this.ErrorResilientMode;
    this.ResetsAllFrameContexts =
      this.NeedsPastIndependence
      && (this.FrameType == KEY_FRAME || this.ErrorResilientMode || this.ResetFrameContext == 3);
    this.ResetsOneFrameContext =
      this.NeedsPastIndependence && !this.ResetsAllFrameContexts && this.ResetFrameContext == 2;
    this.ContextIndexToReset = this.FrameContextIndex;

    if (this.NeedsPastIndependence) {
      this._SetUpPastIndependence();
      this.FrameContextIndex = 0;
    }

    this._ReadLoopFilterParameters(ref reader);
    this._ReadQuantisationParameters(ref reader);
    this._ReadSegmentationParameters(ref reader);
    this._ReadTileInfo(ref reader);

    this.HeaderSizeInBytes = reader.ReadLiteral(16);
    if (this.HeaderSizeInBytes == 0)
      throw new InvalidDataException(
        "This VP9 frame states a compressed header of zero bytes. Specification 6.1 gives that meaning only to a "
        + "frame that shows an already decoded picture, and this frame does not.");
  }

  /// <summary>Whether this frame asked to be decodable without its predecessors (specification 7.2).</summary>
  internal bool NeedsPastIndependence { get; private set; }

  internal bool ResetsAllFrameContexts { get; private set; }
  internal bool ResetsOneFrameContext { get; private set; }

  /// <summary>
  /// The frame context a frame that resets exactly one of them names, read before
  /// <see cref="FrameContextIndex"/> is forced to zero.
  /// </summary>
  internal int ContextIndexToReset { get; private set; }

  // ============================================================================================
  // Header pieces
  // ============================================================================================

  private static void _ReadSyncCode(ref Vp9BitReader reader) {
    var byte0 = reader.ReadLiteral(8);
    var byte1 = reader.ReadLiteral(8);
    var byte2 = reader.ReadLiteral(8);

    if (byte0 != SYNC_BYTE_0 || byte1 != SYNC_BYTE_1 || byte2 != SYNC_BYTE_2)
      throw new InvalidDataException(
        $"This VP9 frame does not carry the sync code 49 83 42 that specification 7.2.1 requires, but "
        + $"{byte0:X2} {byte1:X2} {byte2:X2}.");
  }

  private void _ReadColorConfig(ref Vp9BitReader reader) {
    this.ColorSpace = reader.ReadLiteral(3);

    if (this.ColorSpace == CS_RGB) {
      if (this.Profile != 1)
        throw new NotSupportedException(
          "This VP9 key frame states the sRGB colour space, which specification 7.2.2 permits only in profiles 1 and "
          + "3. A profile 0 stream cannot carry it.");

      this.ColorRange = 1;
      this.SubsamplingX = 0;
      this.SubsamplingY = 0;
      if (reader.ReadBit() != 0)
        throw new InvalidDataException("This VP9 profile-1 sRGB frame sets the reserved_zero bit in color_config().");

      return;
    }

    this.ColorRange = reader.ReadBit();

    if (this.Profile == 0) {
      this.SubsamplingX = 1;
      this.SubsamplingY = 1;
      return;
    }

    this.SubsamplingX = reader.ReadBit();
    this.SubsamplingY = reader.ReadBit();
    if (this.SubsamplingX == 1 && this.SubsamplingY == 1)
      throw new InvalidDataException(
        "This VP9 profile-1 frame states 4:2:0 chroma. Profile 1 exists for the eight-bit non-4:2:0 formats; "
        + "4:2:0 is profile 0.");

    if (reader.ReadBit() != 0)
      throw new InvalidDataException("This VP9 profile-1 frame sets the reserved_zero bit in color_config().");
  }

  private void _ReadFrameSize(ref Vp9BitReader reader) {
    var width = reader.ReadLiteral(16) + 1;
    var height = reader.ReadLiteral(16) + 1;
    this._SetSize(width, height);
  }

  private void _ReadRenderSize(ref Vp9BitReader reader) {
    if (reader.ReadBit() != 0) {
      this.RenderWidth = reader.ReadLiteral(16) + 1;
      this.RenderHeight = reader.ReadLiteral(16) + 1;
    } else {
      this.RenderWidth = this.FrameWidth;
      this.RenderHeight = this.FrameHeight;
    }
  }

  private void _ReadFrameSizeWithReferences(
    ref Vp9BitReader reader, int[] referenceWidths, int[] referenceHeights, bool[] slotIsValid) {
    var found = false;

    for (var i = 0; i < REFS_PER_FRAME; ++i) {
      if (reader.ReadBit() == 0)
        continue;

      var slot = this.ReferenceFrameIndex[i];
      if (!slotIsValid[slot])
        throw new InvalidDataException(
          $"This VP9 inter frame takes its picture size from reference slot {slot}, which no frame of this stream "
          + "has written. Specification 8.2 requires that slot to have been filled by an earlier frame.");

      this._SetSize(referenceWidths[slot], referenceHeights[slot]);
      found = true;
      break;
    }

    if (!found)
      this._ReadFrameSize(ref reader);

    this._ReadRenderSize(ref reader);
  }

  /// <summary>
  /// Records the picture size and derives everything measured in blocks from it
  /// (specification 6.2.6 and 7.2.6).
  /// </summary>
  private void _SetSize(int width, int height) {
    this.FrameWidth = width;
    this.FrameHeight = height;
    this.MiCols = (width + 7) >> 3;
    this.MiRows = (height + 7) >> 3;
    this.Sb64Cols = (this.MiCols + 7) >> 3;
    this.Sb64Rows = (this.MiRows + 7) >> 3;

    var sameSize = this._haveComputedImageSize && width == this._previousWidth && height == this._previousHeight;
    this.SizeChanged = !sameSize;

    this.UsePreviousFrameMotionVectors =
      sameSize && this._previousShowFrame && !this.ErrorResilientMode && !this.FrameIsIntra;

    this._haveComputedImageSize = true;
    this._previousWidth = width;
    this._previousHeight = height;
    this._previousShowFrame = this.ShowFrame;
  }

  private void _ReadInterpolationFilter(ref Vp9BitReader reader)
    => this.InterpolationFilter = reader.ReadBit() != 0
      ? SWITCHABLE
      : Vp9Tables.LiteralToFilterType[reader.ReadLiteral(2)];

  private void _ReadLoopFilterParameters(ref Vp9BitReader reader) {
    this.LoopFilterLevel = reader.ReadLiteral(6);
    this.LoopFilterSharpness = reader.ReadLiteral(3);
    this.LoopFilterDeltaEnabled = reader.ReadBit() != 0;
    this.LoopFilterDeltaUpdate = false;

    if (!this.LoopFilterDeltaEnabled)
      return;

    this.LoopFilterDeltaUpdate = reader.ReadBit() != 0;
    if (!this.LoopFilterDeltaUpdate)
      return;

    for (var i = 0; i < MAX_REF_FRAMES; ++i)
      if (reader.ReadBit() != 0)
        this.LoopFilterReferenceDeltas[i] = reader.ReadSignedLiteral(6);

    for (var i = 0; i < MAX_MODE_LF_DELTAS; ++i)
      if (reader.ReadBit() != 0)
        this.LoopFilterModeDeltas[i] = reader.ReadSignedLiteral(6);
  }

  private void _ReadQuantisationParameters(ref Vp9BitReader reader) {
    this.BaseQIndex = reader.ReadLiteral(8);
    this.DeltaQYDc = _ReadDeltaQ(ref reader);
    this.DeltaQUvDc = _ReadDeltaQ(ref reader);
    this.DeltaQUvAc = _ReadDeltaQ(ref reader);
    this.Lossless = this.BaseQIndex == 0 && this.DeltaQYDc == 0 && this.DeltaQUvDc == 0 && this.DeltaQUvAc == 0;
  }

  private static int _ReadDeltaQ(ref Vp9BitReader reader) => reader.ReadBit() != 0 ? reader.ReadSignedLiteral(4) : 0;

  private void _ReadSegmentationParameters(ref Vp9BitReader reader) {
    this.SegmentationUpdateMap = false;
    this.SegmentationTemporalUpdate = false;

    this.SegmentationEnabled = reader.ReadBit() != 0;
    if (!this.SegmentationEnabled)
      return;

    this.SegmentationUpdateMap = reader.ReadBit() != 0;
    if (this.SegmentationUpdateMap) {
      for (var i = 0; i < 7; ++i)
        this.SegmentationTreeProbabilities[i] = _ReadProbability(ref reader);

      this.SegmentationTemporalUpdate = reader.ReadBit() != 0;
      for (var i = 0; i < 3; ++i)
        this.SegmentationPredictionProbabilities[i] =
          this.SegmentationTemporalUpdate ? _ReadProbability(ref reader) : (byte)255;
    }

    if (reader.ReadBit() == 0)
      return;

    this.SegmentationAbsoluteValues = reader.ReadBit() != 0;
    for (var segment = 0; segment < MAX_SEGMENTS; ++segment)
    for (var feature = 0; feature < SEG_LVL_MAX; ++feature) {
      var value = 0;
      var enabled = reader.ReadBit() != 0;
      this.FeatureEnabled[segment * SEG_LVL_MAX + feature] = enabled;

      if (enabled) {
        value = reader.ReadLiteral(Vp9Tables.SegmentationFeatureBits[feature]);
        if (Vp9Tables.SegmentationFeatureSigned[feature] != 0 && reader.ReadBit() != 0)
          value = -value;
      }

      this.FeatureData[segment * SEG_LVL_MAX + feature] = value;
    }
  }

  private static byte _ReadProbability(ref Vp9BitReader reader)
    => reader.ReadBit() != 0 ? (byte)reader.ReadLiteral(8) : (byte)255;

  private void _ReadTileInfo(ref Vp9BitReader reader) {
    var minimum = 0;
    while ((MAX_TILE_WIDTH_B64 << minimum) < this.Sb64Cols)
      ++minimum;

    var maximum = 1;
    while (this.Sb64Cols >> maximum >= MIN_TILE_WIDTH_B64)
      ++maximum;
    --maximum;

    this.TileColsLog2 = minimum;
    while (this.TileColsLog2 < maximum) {
      if (reader.ReadBit() == 0)
        break;

      ++this.TileColsLog2;
    }

    this.TileRowsLog2 = reader.ReadBit();
    if (this.TileRowsLog2 == 1)
      this.TileRowsLog2 += reader.ReadBit();
  }

  /// <summary>
  /// Forgets everything a frame may have inherited from the frames before it (specification 7.2).
  /// </summary>
  /// <remarks>
  /// The reference deltas do not reset to zero. Intra blocks are filtered a step harder and the two
  /// long-term references a step softer, which is the format's standing opinion that an intra block
  /// is where the blocking artefacts are and a golden frame is where they are not.
  /// </remarks>
  private void _SetUpPastIndependence() {
    Array.Clear(this.FeatureEnabled);
    Array.Clear(this.FeatureData);
    this.SegmentationAbsoluteValues = false;

    this.LoopFilterDeltaEnabled = true;
    this.LoopFilterReferenceDeltas[INTRA_FRAME] = 1;
    this.LoopFilterReferenceDeltas[LAST_FRAME] = 0;
    this.LoopFilterReferenceDeltas[GOLDEN_FRAME] = -1;
    this.LoopFilterReferenceDeltas[ALTREF_FRAME] = -1;
    Array.Clear(this.LoopFilterModeDeltas);

    Array.Clear(this.ReferenceFrameSignBias);
  }

  /// <summary>
  /// Works out which reference is fixed and which two are chosen between, in compound prediction
  /// (specification 6.3.18).
  /// </summary>
  internal void SetUpCompoundReferenceMode() {
    if (this.ReferenceFrameSignBias[LAST_FRAME] == this.ReferenceFrameSignBias[GOLDEN_FRAME]) {
      this.CompoundFixedReference = ALTREF_FRAME;
      this.CompoundVariableReference[0] = LAST_FRAME;
      this.CompoundVariableReference[1] = GOLDEN_FRAME;
    } else if (this.ReferenceFrameSignBias[LAST_FRAME] == this.ReferenceFrameSignBias[ALTREF_FRAME]) {
      this.CompoundFixedReference = GOLDEN_FRAME;
      this.CompoundVariableReference[0] = LAST_FRAME;
      this.CompoundVariableReference[1] = ALTREF_FRAME;
    } else {
      this.CompoundFixedReference = LAST_FRAME;
      this.CompoundVariableReference[0] = GOLDEN_FRAME;
      this.CompoundVariableReference[1] = ALTREF_FRAME;
    }
  }
}
