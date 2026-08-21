namespace FileFormat.Codecs.Mpeg;

/// <summary>
/// The choices a picture makes that the block layer has to obey: which standard, which scan, which
/// coefficient table and how finely the intra DC is coded.
/// </summary>
/// <remarks>
/// All four are constant for the length of one picture and none of them is derivable from the bits
/// of a block, so they travel together rather than as four parameters that could be passed in the
/// wrong order or forgotten one at a time. Three of the four exist only in MPEG-2; an MPEG-1 picture
/// builds this with the one set of answers that standard allows.
/// </remarks>
internal sealed class MpegBlockRules {

  /// <summary>Whether the dequantisation and the escape coding are 13818-2's or 11172-2's.</summary>
  internal required bool IsMpeg2 { get; init; }

  /// <summary>
  /// <c>intra_vlc_format</c>: whether intra blocks read their coefficients from Table B.15 rather
  /// than Table B.14 (13818-2, Table 7-3).
  /// </summary>
  internal required bool UseIntraCoefficientTable { get; init; }

  /// <summary>Scan position to raster position: the zig-zag, or the alternate scan.</summary>
  internal required int[] Scan { get; init; }

  /// <summary>
  /// What an intra DC level is multiplied by (13818-2, Table 7-1): eight, four, two or one as
  /// <c>intra_dc_precision</c> says the DC is coded to eight, nine, ten or eleven bits.
  /// </summary>
  internal required int IntraDcMultiplier { get; init; }

  /// <summary>
  /// What the intra DC predictors are reset to, which is half of the range the chosen precision
  /// covers (13818-2, 7.2.1).
  /// </summary>
  internal int IntraDcPredictorReset => 1024 / this.IntraDcMultiplier;

  /// <summary>The table intra blocks read their DC size from, for a luminance or a chrominance block.</summary>
  internal MpegVlcTable DcSizeTable(bool isChroma) => this.IsMpeg2
    ? isChroma ? MpegVlcTables.Mpeg2ChrominanceDcSize : MpegVlcTables.Mpeg2LuminanceDcSize
    : isChroma ? MpegVlcTables.Mpeg1ChrominanceDcSize : MpegVlcTables.Mpeg1LuminanceDcSize;

  /// <summary>The MPEG-1 rules, which are the only ones that standard has.</summary>
  internal static MpegBlockRules ForMpeg1() => new() {
    IsMpeg2 = false,
    UseIntraCoefficientTable = false,
    Scan = MpegQuantisation.ZigZagScan,
    IntraDcMultiplier = 8,
  };
}
