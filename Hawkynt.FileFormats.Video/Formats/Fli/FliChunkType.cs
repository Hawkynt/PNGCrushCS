namespace FileFormat.FlicVideo;

/// <summary>
/// The sub-chunk type codes a FLIC <c>FRAME_TYPE</c> chunk carries, as the Autodesk Animator file
/// format specification and the broader FLIC family reference list them.
/// </summary>
/// <remarks>
/// Shared between the container, which only needs to recognise a whole-frame picture chunk well
/// enough to mark a frame as a key frame, and the codec, which decodes every renderable image chunk
/// it supports.
/// </remarks>
internal static class FliChunkType {

  /// <summary>256-level palette update; RGB components are eight bits each.</summary>
  internal const ushort COLOR256 = 4;

  /// <summary>Word-oriented delta compression, the form <c>.flc</c> writes.</summary>
  internal const ushort SS2 = 7;

  /// <summary>64-level palette update; RGB components are six bits each and are widened to eight.</summary>
  internal const ushort COLOR64 = 11;

  /// <summary>Byte-oriented delta compression, the form the original <c>.fli</c> writes.</summary>
  internal const ushort LC = 12;

  /// <summary>The whole frame is zero. Carries no data of its own.</summary>
  internal const ushort BLACK = 13;

  /// <summary>Byte-run length compression of a whole frame, used for the first frame of a file.</summary>
  internal const ushort BRUN = 15;

  /// <summary>An uncompressed whole frame, top row first.</summary>
  internal const ushort COPY = 16;

  /// <summary>A postage-stamp thumbnail of the film, for a file requestor. Not a picture of the film.</summary>
  internal const ushort PSTAMP = 18;

  /// <summary>DTA whole-frame RLE whose counts are pixels rather than bytes.</summary>
  internal const ushort DTA_BRUN = 25;

  /// <summary>DTA uncompressed whole frame for 15-, 16- and 24-bit pixels.</summary>
  internal const ushort DTA_COPY = 26;

  /// <summary>DTA pixel-oriented delta compression for 15-, 16- and 24-bit pixels.</summary>
  internal const ushort DTA_LC = 27;
}
