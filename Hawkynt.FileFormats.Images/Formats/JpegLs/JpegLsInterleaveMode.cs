namespace FileFormat.JpegLs;

/// <summary>Interleave mode for multi-component images in JPEG-LS.</summary>
public enum JpegLsInterleaveMode : byte {
  /// <summary>Non-interleaved: each component encoded in a separate scan.</summary>
  None = 0,
  /// <summary>Line-interleaved: components interleaved line by line.</summary>
  Line = 1,
  /// <summary>Sample-interleaved: components interleaved sample by sample.</summary>
  Sample = 2,
}
