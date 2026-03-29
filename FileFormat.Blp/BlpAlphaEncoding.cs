namespace FileFormat.Blp;

/// <summary>Specifies the DXT alpha encoding variant in a BLP2 file.</summary>
public enum BlpAlphaEncoding : byte {
  /// <summary>DXT1 (BC1) compression -- no explicit alpha or 1-bit punch-through.</summary>
  Dxt1 = 0,
  /// <summary>DXT3 (BC2) compression -- explicit 4-bit alpha per pixel.</summary>
  Dxt3 = 1,
  /// <summary>DXT5 (BC3) compression -- interpolated 8-bit alpha.</summary>
  Dxt5 = 7,
}
