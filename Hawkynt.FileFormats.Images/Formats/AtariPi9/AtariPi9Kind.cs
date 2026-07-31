namespace FileFormat.AtariPi9;

/// <summary>Which of the three pictures a .pi9 file holds.</summary>
public enum AtariPi9Kind {

  /// <summary>An Atari 8-bit Graphics 9 screen: sixteen luminances of one hue.</summary>
  Graphics9,

  /// <summary>An APAC screen: luminances and hues on alternate scanlines.</summary>
  Apac,

  /// <summary>A Falcon picture: eight bitplanes against 256 freely chosen colours.</summary>
  Falcon,
}
