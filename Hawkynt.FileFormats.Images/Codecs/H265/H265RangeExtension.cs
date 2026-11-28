namespace FileFormat.Codecs.H265;

/// <summary>
/// The nine coding tools a sequence switches on in <c>sps_range_extension()</c> — ITU-T H.265,
/// clauses 7.3.2.2.2 and 7.4.3.2.2.
/// </summary>
/// <remarks>
/// The format range extensions were written for content the first version had no profile for:
/// screen captures, 4:4:4 and RGB, lossless coding, and sample depths up to sixteen bits. Almost all
/// of what they add is a change to how a residual block is coded or put back together, which is why
/// these flags are consulted from the residual syntax and the reconstruction rather than from
/// anywhere in prediction.
/// </remarks>
internal sealed record H265RangeExtension {

  /// <summary>The sequence that sends no range extension: every tool off.</summary>
  internal static H265RangeExtension None { get; } = new();

  /// <summary>
  /// <c>transform_skip_rotation_enabled_flag</c>: an intra 4x4 block coded without its transform is
  /// rotated by half a turn, so that its energy sits at the end of the scan the coefficient coding
  /// expects it at the start of.
  /// </summary>
  internal bool TransformSkipRotationEnabled { get; init; }

  /// <summary>
  /// <c>transform_skip_context_enabled_flag</c>: a block without a transform codes its significance
  /// flags in one context of its own, because its residual has no frequency structure for the usual
  /// position-dependent contexts to model.
  /// </summary>
  internal bool TransformSkipContextEnabled { get; init; }

  /// <summary>
  /// <c>implicit_rdpcm_enabled_flag</c>: an intra block predicted straight across or straight down
  /// and coded without a transform has its residual accumulated along the prediction direction.
  /// </summary>
  internal bool ImplicitRdpcmEnabled { get; init; }

  /// <summary>
  /// <c>explicit_rdpcm_enabled_flag</c>: an inter block coded without a transform may say, per
  /// component, that its residual is accumulated horizontally or vertically.
  /// </summary>
  internal bool ExplicitRdpcmEnabled { get; init; }

  /// <summary>
  /// <c>extended_precision_processing_flag</c>: coefficients and the transform's intermediate values
  /// get a dynamic range that grows with the sample depth, and the escape coding of large levels is
  /// bounded to fit it.
  /// </summary>
  internal bool ExtendedPrecisionProcessing { get; init; }

  /// <summary><c>intra_smoothing_disabled_flag</c>: intra reference samples are never filtered.</summary>
  internal bool IntraSmoothingDisabled { get; init; }

  /// <summary>
  /// <c>high_precision_offsets_enabled_flag</c>: weighted prediction offsets are stated at the
  /// sample depth itself instead of at eight bits and scaled up.
  /// </summary>
  internal bool HighPrecisionOffsetsEnabled { get; init; }

  /// <summary>
  /// <c>persistent_rice_adaptation_enabled_flag</c>: the Rice parameter each sub-block starts from
  /// carries over from the levels earlier sub-blocks coded, kept as <c>StatCoeff</c>.
  /// </summary>
  internal bool PersistentRiceAdaptationEnabled { get; init; }

  /// <summary>
  /// <c>cabac_bypass_alignment_enabled_flag</c>: the arithmetic decoder's range is reset to 256 before
  /// the bypass-coded signs and escapes of a sub-block, which turns them into plain bits.
  /// </summary>
  internal bool CabacBypassAlignmentEnabled { get; init; }
}
