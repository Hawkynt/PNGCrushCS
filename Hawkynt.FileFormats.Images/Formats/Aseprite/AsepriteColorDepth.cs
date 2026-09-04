namespace FileFormat.Aseprite;

/// <summary>The bits a pixel takes in an Aseprite sprite, which also says what a pixel means.</summary>
public enum AsepriteColorDepth {

  /// <summary>One byte: an index into the sprite's palette.</summary>
  Indexed = 8,

  /// <summary>Two bytes: a grey value and an alpha.</summary>
  Grayscale = 16,

  /// <summary>Four bytes: red, green, blue and alpha.</summary>
  Rgba = 32,

}
