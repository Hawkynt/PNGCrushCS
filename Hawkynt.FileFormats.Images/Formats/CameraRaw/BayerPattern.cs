namespace FileFormat.CameraRaw;

/// <summary>Bayer CFA pattern indicating which color filter is on each pixel.</summary>
public enum BayerPattern {

  /// <summary>Row 0: R G, Row 1: G B.</summary>
  RGGB = 0,

  /// <summary>Row 0: B G, Row 1: G R.</summary>
  BGGR = 1,

  /// <summary>Row 0: G R, Row 1: B G.</summary>
  GRBG = 2,

  /// <summary>Row 0: G B, Row 1: R G.</summary>
  GBRG = 3,
}
