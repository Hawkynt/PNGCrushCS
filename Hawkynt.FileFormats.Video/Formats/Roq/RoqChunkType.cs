namespace FileFormat.RoqVideo;

/// <summary>
/// The chunk identifiers a RoQ file's flat run of chunks names itself with, from Dr. Tim Ferguson's
/// reverse-engineered description of the format and confirmed against every sample this was built
/// against.
/// </summary>
internal static class RoqChunkType {

  /// <summary>The eight fixed bytes every RoQ file opens with, spelled out as a chunk of its own —
  /// id <c>0x1084</c>, a length of <c>0xFFFFFFFF</c> that states nothing rather than a real size, and
  /// an argument of <c>0x001E</c>. Present for identification only; never handed out as a packet.</summary>
  internal const ushort SIGNATURE = 0x1084;

  /// <summary>Picture width, height, and two fields the format's own documentation calls unused.</summary>
  internal const ushort INFO = 0x1001;

  /// <summary>Up to 256 2x2 colour cells and up to 256 4x4 cells built from four of them apiece.</summary>
  internal const ushort QUAD_CODEBOOK = 0x1002;

  /// <summary>The quadtree-coded picture: the only chunk that ever produces a frame.</summary>
  internal const ushort QUAD_VQ = 0x1011;

  /// <summary>A whole JFIF file in place of a quadtree-coded picture — the 11th Hour/Clandestiny
  /// superset of the format, not read here.</summary>
  internal const ushort JPEG = 0x1012;

  /// <summary>A housekeeping marker with no picture or sound in it.</summary>
  internal const ushort HANG = 0x1013;

  /// <summary>Single-channel id RoQ DPCM sound.</summary>
  internal const ushort SOUND_MONO = 0x1020;

  /// <summary>Two-channel id RoQ DPCM sound, interleaved left, right, left, right.</summary>
  internal const ushort SOUND_STEREO = 0x1021;

  /// <summary>A hint that this is a good place to read ahead; carries no data of its own.</summary>
  internal const ushort PACKET = 0x1030;
}
