using System.IO;

namespace FileFormat.Codecs.H265;

/// <summary>
/// The screen content coding tools a sequence switches on in <c>sps_scc_extension()</c> — ITU-T
/// H.265, clauses 7.3.2.2.3 and 7.4.3.2.3.
/// </summary>
/// <remarks>
/// The screen content extensions are for what cameras do not produce: text, window borders, flat
/// colour and the same few pixels repeated across a desktop. Each tool here answers one of those.
/// A coding unit may copy a block of the picture it is part of, the way a camera picture copies a
/// block of an earlier one; it may name a handful of colours and say which one each sample is; and a
/// sequence may ask for its motion vectors in whole samples, because a scrolled window moves by
/// whole samples and a fraction is bits spent on nothing.
/// </remarks>
internal sealed record H265ScreenContentExtension {

  /// <summary>The sequence that sends no screen content extension: every tool off.</summary>
  internal static H265ScreenContentExtension None { get; } = new();

  /// <summary>
  /// <c>sps_curr_pic_ref_enabled_flag</c>: a picture may name itself in its own reference lists,
  /// which is how intra block copy is coded — as inter prediction from the part of the current
  /// picture already decoded.
  /// </summary>
  internal bool CurrentPictureReferenceEnabled { get; init; }

  /// <summary><c>palette_mode_enabled_flag</c>: an intra coding unit may be coded as palette indices.</summary>
  internal bool PaletteModeEnabled { get; init; }

  /// <summary><c>palette_max_size</c>: the most colours one coding unit's palette may hold.</summary>
  internal int PaletteMaxSize { get; init; }

  /// <summary><c>PaletteMaxPredictorSize</c>: the most colours the predictor carried between coding units may hold.</summary>
  internal int PaletteMaxPredictorSize { get; init; }

  /// <summary>
  /// <c>sps_palette_predictor_initializer</c>, one array per colour component, or <c>null</c> where
  /// the sequence starts every predictor empty.
  /// </summary>
  internal int[][]? PalettePredictorInitializers { get; init; }

  /// <summary>
  /// <c>motion_vector_resolution_control_idc</c>: 0 keeps quarter-sample motion vectors, 1 makes
  /// every vector a whole number of samples, and 2 lets each slice choose.
  /// </summary>
  internal int MotionVectorResolutionControlIdc { get; init; }

  /// <summary>
  /// <c>intra_boundary_filtering_disabled_flag</c>: the edge smoothing of the DC, horizontal and
  /// vertical intra modes is never applied, because on text it blurs the one row that matters.
  /// </summary>
  internal bool IntraBoundaryFilteringDisabled { get; init; }

  /// <summary>Reads <c>sps_scc_extension()</c>.</summary>
  internal static H265ScreenContentExtension Parse(
    ref H265BitReader reader, int chromaFormatIdc, int bitDepthLuma, int bitDepthChroma) {
    var currentPictureReference = reader.ReadFlag();
    var paletteMode = reader.ReadFlag();

    var maxSize = 0;
    var maxPredictorSize = 0;
    int[][]? initializers = null;
    if (paletteMode) {
      maxSize = reader.ReadUnsignedExpGolomb();
      maxPredictorSize = maxSize + reader.ReadUnsignedExpGolomb();

      // Annex A bounds both for every profile that has palette mode at all.
      if (maxSize > 64 || maxPredictorSize > 128)
        throw new InvalidDataException(
          $"An H.265 sequence parameter set allows palettes of {maxSize} colours and a predictor of "
          + $"{maxPredictorSize}. Annex A bounds them at 64 and 128.");

      if (reader.ReadFlag()) {
        var count = reader.ReadUnsignedExpGolomb() + 1;
        if (count > maxPredictorSize)
          throw new InvalidDataException(
            $"An H.265 sequence parameter set initialises its palette predictor with {count} colours, more than the "
            + $"{maxPredictorSize} clause 7.4.3.2.3 lets the predictor hold.");

        initializers = ReadEntries(
          ref reader, chromaFormatIdc == 0 ? 1 : 3, count, bitDepthLuma, bitDepthChroma);
      }
    }

    var resolution = reader.ReadBits(2);
    if (resolution == 3)
      throw new InvalidDataException(
        "An H.265 sequence parameter set states motion_vector_resolution_control_idc 3, which clause 7.4.3.2.3 "
        + "reserves.");

    return new() {
      CurrentPictureReferenceEnabled = currentPictureReference,
      PaletteModeEnabled = paletteMode,
      PaletteMaxSize = maxSize,
      PaletteMaxPredictorSize = maxPredictorSize,
      PalettePredictorInitializers = initializers,
      MotionVectorResolutionControlIdc = resolution,
      IntraBoundaryFilteringDisabled = reader.ReadFlag(),
    };
  }

  /// <summary>
  /// Reads palette predictor initialisers: every entry's first component, then every entry's
  /// second, then every entry's third, each at its component's depth.
  /// </summary>
  internal static int[][] ReadEntries(
    ref H265BitReader reader, int components, int count, int bitDepthLuma, int bitDepthChroma) {
    var entries = new int[components][];
    for (var c = 0; c < components; ++c) {
      entries[c] = new int[count];
      var bits = c == 0 ? bitDepthLuma : bitDepthChroma;
      for (var i = 0; i < count; ++i)
        entries[c][i] = reader.ReadBits(bits);
    }

    return entries;
  }
}
