namespace FileFormat.Ea;

/// <summary>
/// The four-character chunk identifiers an Electronic Arts multimedia file's flat chunk run is built
/// from, drawn from Electronic Arts' own family-wide block structure — every chunk a four-byte FourCC,
/// a four-byte little-endian size that includes those first eight bytes, and a payload — and from the
/// per-codec chunk names each of EA's own video codecs is known by within it.
/// </summary>
/// <remarks>
/// Only the chunk kinds this reader gives any meaning to are named here. Everything else — every audio
/// chunk, every codec this project does not decode — costs nothing to step over: <see cref="EaReader"/>
/// walks past an unrecognised chunk by its own stated size and never needs to know its name at all.
/// </remarks>
internal static class EaChunkType {

  // ---- Electronic Arts CMV: NHL 95's own cinematics ----

  /// <summary>Picture dimensions, frame rate and (some or all of) the palette. May recur through a
  /// file, each occurrence restating the palette from that point on — nothing states a whole file
  /// carries only one.</summary>
  internal const uint MVIh = 0x6849564D; // "MVIh"

  /// <summary>One coded picture: a two-byte frame type, then the picture's own coded bytes.</summary>
  internal const uint MVIf = 0x6649564D; // "MVIf"

  /// <summary>Ends one CMV video stream. A file may hold more than one — see <see
  /// cref="EaReader"/>'s remarks on <c>TITLE.CMV</c>, which restarts with a fresh <see cref="MVIh"/>
  /// immediately after.</summary>
  internal const uint MVIe = 0x6549564D; // "MVIe"

  // ---- Electronic Arts TGV ----

  /// <summary>An intra-coded TGV picture: dimensions, palette and a losslessly compressed raster, all
  /// in the one chunk.</summary>
  internal const uint kVGT = 0x5447566B; // "kVGT"

  /// <summary>An inter-coded TGV picture: a motion vector, raw and vector-quantised code book, and a
  /// per-block index table.</summary>
  internal const uint fVGT = 0x54475666; // "fVGT"

  // ---- Chunk families this reader recognises by name only, so it can skip them without guessing
  //      at a size ----

  internal const uint SCHl = 0x6C484353; // "SCHl" — EA sound stream header
  internal const uint SEAD = 0x44414553; // "SEAD" — EA sound stream header, the form TGV's own samples use

  internal static bool IsCmv(uint fourCc) => fourCc is MVIh or MVIf or MVIe;

  internal static bool IsTgv(uint fourCc) => fourCc is kVGT or fVGT;
}
