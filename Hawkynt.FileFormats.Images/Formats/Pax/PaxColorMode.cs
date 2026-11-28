namespace FileFormat.Pax;

/// <summary>How a Pick Ax row stores a pixel; the value is the header's bytes-per-pixel field.</summary>
public enum PaxColorMode {
  /// <summary>One byte, an index into a palette of up to 256 colours.</summary>
  Indexed8 = 1,
  /// <summary>Two bytes, a little-endian <c>xRRRRRGGGGGBBBBB</c> word.</summary>
  Rgb555 = 2,
  /// <summary>Three bytes, red, green, blue.</summary>
  Rgb24 = 3,
}
