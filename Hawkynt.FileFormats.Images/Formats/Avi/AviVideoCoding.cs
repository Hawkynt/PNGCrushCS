namespace FileFormat.Avi;

/// <summary>How the frames of an AVI video stream are stored — the two this reader can undo.</summary>
/// <remarks>
/// Deliberately short. Every other four-character code an AVI can name is a codec nothing here
/// decodes, and a reader that guessed at one would hand back noise while reporting success. Those
/// are refused by name instead; see <see cref="AviReader"/>.
/// </remarks>
public enum AviVideoCoding {

  /// <summary>
  /// <c>BI_RGB</c>: each frame chunk is the pixel array of a Windows DIB and nothing else — no file
  /// header, no info header, rows padded to four bytes, bottom-up unless <c>biHeight</c> is negative.
  /// </summary>
  Uncompressed,

  /// <summary>
  /// <c>MJPG</c>/<c>mjpg</c>: each frame chunk is a complete JPEG, <c>FF D8</c> through <c>FF D9</c>.
  /// </summary>
  MotionJpeg,
}
