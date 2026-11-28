namespace FileFormat.MrSid;

/// <summary>The MrSID raster generation a file was written in.</summary>
public enum MrSidGeneration {

  /// <summary>Generation 2: version byte 1, big-endian fixed header with a per-level offset table and JPEG-like markers.</summary>
  Mg2 = 2,

  /// <summary>Generation 3: version byte 3 with a minor version below 4, a fixed header followed by length-prefixed boxes.</summary>
  Mg3 = 3,

  /// <summary>Generation 4 raster: the generation 3 container, minor version 4, metadata stored after the codestream.</summary>
  Mg4 = 4,
}
