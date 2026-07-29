namespace FileFormat.ExtendedGemImg;

/// <summary>Color model used by the XIMG extension header.</summary>
public enum ExtendedGemImgColorModel {
  /// <summary>RGB color model (standard).</summary>
  Rgb = 0,
  /// <summary>CMY color model.</summary>
  Cmy = 1,
  /// <summary>Pantone/HLS color model.</summary>
  Pantone = 2
}
