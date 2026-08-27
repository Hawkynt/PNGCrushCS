namespace FileFormat.Ea;

/// <summary>
/// The four-character chunk identifiers an Electronic Arts multimedia file's flat chunk run is built
/// from, drawn from Electronic Arts' own family-wide block structure — every chunk a four-byte FourCC,
/// a four-byte little-endian size that includes those first eight bytes, and a payload — and from the
/// per-codec chunk names each of EA's own video and audio families is known by within it.
/// </summary>
internal static class EaChunkType {

  // ---- Electronic Arts CMV ----
  internal const uint MVIh = 0x6849564D; // "MVIh"
  internal const uint MVIf = 0x6649564D; // "MVIf"
  internal const uint MVIe = 0x6549564D; // "MVIe"

  // ---- Electronic Arts TGV ----
  internal const uint kVGT = 0x5447566B; // "kVGT"
  internal const uint fVGT = 0x54475666; // "fVGT"

  // ---- Electronic Arts SEAD ----
  internal const uint SEAD = 0x44414553; // "SEAD" header
  internal const uint SNDC = 0x43444E53; // "SNDC" data
  internal const uint SEND = 0x444E4553; // "SEND" end

  // ---- Electronic Arts 1SNx ----
  internal const uint _1SNh = 0x684E5331; // "1SNh" header
  internal const uint _1SNd = 0x644E5331; // "1SNd" data
  internal const uint _1SNl = 0x6C4E5331; // "1SNl" loop
  internal const uint _1SNe = 0x654E5331; // "1SNe" end

  // ---- Electronic Arts SCxl and later reversed-name variants ----
  internal const uint SCHl = 0x6C484353; // "SCHl" header
  internal const uint SCCl = 0x6C434353; // "SCCl" data-block count
  internal const uint SCDl = 0x6C444353; // "SCDl" data
  internal const uint SCLl = 0x6C4C4353; // "SCLl" loop
  internal const uint SCEl = 0x6C454353; // "SCEl" end
  internal const uint SHEN = 0x4E454853; // "SHEN" header
  internal const uint SCEN = 0x4E454353; // "SCEN" count
  internal const uint SDEN = 0x4E454453; // "SDEN" data
  internal const uint SEEN = 0x4E454553; // "SEEN" end

  internal static bool IsCmv(uint fourCc) => fourCc is MVIh or MVIf or MVIe;

  internal static bool IsTgv(uint fourCc) => fourCc is kVGT or fVGT;

  /// <summary>
  /// Whether a chunk is structural or coded data belonging to one of EA's documented sound families.
  /// The container does not parse the nested codec patch headers; complete chunks are preserved so a
  /// remux neither drops them nor has to guess which of EA's several audio codecs they describe.
  /// </summary>
  internal static bool IsAudio(uint fourCc)
    => fourCc is SEAD or SNDC or SEND
      or _1SNh or _1SNd or _1SNl or _1SNe
      or SCHl or SCCl or SCDl or SCLl or SCEl
      or SHEN or SCEN or SDEN or SEEN;
}
