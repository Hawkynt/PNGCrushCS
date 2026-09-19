namespace FileFormat.RoqVideo;

/// <summary>Chunk identifiers used by standard id RoQ and the older Trilobyte extensions.</summary>
internal static class RoqChunkType {
  internal const ushort SIGNATURE = 0x1084;
  internal const ushort INFO = 0x1001;
  internal const ushort QUAD_CODEBOOK = 0x1002;
  internal const ushort QUAD_VQ = 0x1011;

  /// <summary>A complete JFIF/JPEG intraframe used by the 11th Hour/Clandestiny RoQ variant.</summary>
  internal const ushort JPEG = 0x1012;

  /// <summary>Repeats the currently displayed picture without advancing the two prediction buffers.</summary>
  internal const ushort HANG = 0x1013;

  internal const ushort SOUND_MONO = 0x1020;
  internal const ushort SOUND_STEREO = 0x1021;

  /// <summary>Older read-ahead/container marker. Its following bytes remain ordinary RoQ chunks.</summary>
  internal const ushort PACKET = 0x1030;
}
