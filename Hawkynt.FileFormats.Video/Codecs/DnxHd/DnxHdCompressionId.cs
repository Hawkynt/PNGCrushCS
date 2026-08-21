using System.Collections.Generic;

namespace FileFormat.Codecs.DnxHd;

/// <summary>
/// What a compression identifier selects — SMPTE ST 2019-1:2016, Annex C, Tables C.1 and C.2.
/// </summary>
/// <remarks>
/// The compression identifier is the one number in a VC-3 frame that a decoder cannot work out from
/// anything else. It is not a bitrate and not a raster: it names a row of Annex C, and that row says
/// which quantisation weighting table of Annex D and which group of variable-length code tables of
/// Annex E the frame was coded with. Two frames of the same size and depth coded under different
/// identifiers use different tables and decode to different pictures, so guessing is not an option
/// and an unknown identifier is refused.
/// <para/>
/// The raster and the sample depth are in the header as well as in this table (7.2.3), and the
/// header is what is read — a decoder that took them from here would be unable to read the
/// resolution-independent identifiers at all, since Table C.2 states no raster for them. What the
/// table is used for is the two things the header does not carry.
/// <para/>
/// <b>Two profiles.</b> Table C.1 is the HD profile: fixed rasters, constant bitrate, header version
/// 1 or 2. Table C.2 is the resolution-independent profile — what Avid sells as DNxHR — which takes
/// its raster from the header, allows variable bitrate, and carries header version 3. Both are read
/// here; they differ in the frame header and not in the block layer.
/// </remarks>
internal sealed class DnxHdCompressionId {

  /// <summary>The identifier itself, as it appears at offset 0x28 of the header.</summary>
  internal required int Id { get; init; }

  /// <summary>The index into <see cref="DnxHdWeightTables"/>: 0 is Table D.1, 10 is Table D.11.</summary>
  internal required int WeightTable { get; init; }

  /// <summary>The group index into <see cref="DnxHdVlcTables"/>: 0 is Tables E.1 to E.3.</summary>
  internal required int VlcGroup { get; init; }

  /// <summary>
  /// The divisor <c>p</c> of the inverse quantisation, SMPTE ST 2019-1:2016, 8.2.7.
  /// </summary>
  /// <remarks>
  /// Eight for three of the identifiers and thirty-two for the rest, and nothing in the bitstream
  /// says which — it follows from the identifier alone. Using the wrong one scales every AC
  /// coefficient of every block by four, which is a picture with its detail either flattened or
  /// blown out, and still a picture.
  /// </remarks>
  internal required int InverseQuantisationDivisor { get; init; }

  /// <summary>Whether this identifier belongs to the resolution-independent profile of Table C.2.</summary>
  internal required bool ResolutionIndependent { get; init; }

  /// <summary>Whether Annex C states this identifier as interlaced.</summary>
  internal required bool Interlaced { get; init; }

  /// <summary>
  /// Every identifier the two tables define, by number.
  /// </summary>
  /// <remarks>
  /// The weight-table and code-group columns are Annex C's own, read across from each row: 1237 for
  /// instance names Table D.2 and Tables E.4 to E.6, which are indices 1 and 1 here.
  /// </remarks>
  private static readonly Dictionary<int, DnxHdCompressionId> _ById = _Build();

  private static Dictionary<int, DnxHdCompressionId> _Build() {
    var table = new Dictionary<int, DnxHdCompressionId>();

    // Table C.1 — the HD profile.
    _Add(table, 1235, weights: 0, vlc: 0, divisor: 8, interlaced: false);
    _Add(table, 1237, weights: 1, vlc: 1, divisor: 32, interlaced: false);
    _Add(table, 1238, weights: 2, vlc: 2, divisor: 32, interlaced: false);
    _Add(table, 1241, weights: 3, vlc: 0, divisor: 8, interlaced: true);
    _Add(table, 1242, weights: 4, vlc: 1, divisor: 32, interlaced: true);
    _Add(table, 1243, weights: 5, vlc: 2, divisor: 32, interlaced: true);
    _Add(table, 1244, weights: 6, vlc: 1, divisor: 32, interlaced: true);
    _Add(table, 1250, weights: 7, vlc: 3, divisor: 8, interlaced: false);
    _Add(table, 1251, weights: 8, vlc: 4, divisor: 32, interlaced: false);
    _Add(table, 1252, weights: 9, vlc: 5, divisor: 32, interlaced: false);
    _Add(table, 1253, weights: 1, vlc: 1, divisor: 32, interlaced: false);
    _Add(table, 1256, weights: 10, vlc: 0, divisor: 32, interlaced: false);
    _Add(table, 1258, weights: 9, vlc: 5, divisor: 32, interlaced: false);
    _Add(table, 1259, weights: 1, vlc: 1, divisor: 32, interlaced: false);
    _Add(table, 1260, weights: 6, vlc: 1, divisor: 32, interlaced: true);

    // Table C.2 — the resolution-independent profile.
    _Add(table, 1270, weights: 10, vlc: 0, divisor: 32, interlaced: false, independent: true);

    // 1271 is the one row of Annex C that does not match the bitstreams. Table C.2 names Table D.1
    // for it; every DNxHR HQX frame measured is quantised with Table D.4 — the table Annex C gives
    // to compression ID 1241 — and only with that one does it decode.
    //
    // This is not a guess between two plausible readings. Sweeping all eleven weighting tables
    // against ffmpeg's decode of the same frames picks out one table per identifier, and for every
    // other identifier the one it picks is the one Annex C names: 1272 picks D.3, 1273 picks D.2,
    // 1270 picks D.11, and 1235 — which Annex C also sends to D.1 — picks D.1. Only 1271 picks
    // something else, and it does so by a margin that is not arguable: with D.4 no sample of a
    // 1920x1080 frame differs from the reference decode by more than 3 of 1023, and with D.1 the
    // worst differs by 103, at every divisor the standard defines and at every other divisor tried.
    _Add(table, 1271, weights: 3, vlc: 0, divisor: 32, interlaced: false, independent: true);
    _Add(table, 1272, weights: 2, vlc: 2, divisor: 32, interlaced: false, independent: true);
    _Add(table, 1273, weights: 1, vlc: 1, divisor: 32, interlaced: false, independent: true);
    _Add(table, 1274, weights: 1, vlc: 1, divisor: 32, interlaced: false, independent: true);

    return table;
  }

  private static void _Add(
    Dictionary<int, DnxHdCompressionId> table, int id, int weights, int vlc, int divisor, bool interlaced,
    bool independent = false)
    => table[id] = new() {
      Id = id,
      WeightTable = weights,
      VlcGroup = vlc,
      InverseQuantisationDivisor = divisor,
      ResolutionIndependent = independent,
      Interlaced = interlaced,
    };

  /// <summary>The row for an identifier, or <c>null</c> when Annex C defines none.</summary>
  internal static DnxHdCompressionId? Find(int id) => _ById.GetValueOrDefault(id);
}
