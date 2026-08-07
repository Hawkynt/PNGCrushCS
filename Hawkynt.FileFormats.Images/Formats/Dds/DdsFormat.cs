namespace FileFormat.Dds;

public enum DdsFormat {
  Unknown = 0,
  Dxt1 = 1,
  Dxt3 = 2,
  Dxt5 = 3,
  Dx10 = 4,
  Rgb = 5,
  Rgba = 6,
  Bc4 = 7,
  Bc5 = 8,
  Bc6HUnsigned = 9,
  Bc6HSigned = 10,
  Bc7 = 11,

  /// <summary>
  /// One uncompressed byte a pixel, drawn as grey.
  /// </summary>
  /// <remarks>
  /// A surface flagged alpha-only or luminance-only rather than RGB. Neither flag was looked at, so
  /// such a file came back as an unsupported format having stated its size, its depth and its whole
  /// picture plainly.
  /// </remarks>
  Single8 = 12,
}
