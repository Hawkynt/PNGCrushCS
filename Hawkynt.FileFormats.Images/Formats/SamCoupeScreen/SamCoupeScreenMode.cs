namespace FileFormat.SamCoupeScreen;

/// <summary>The three SAM Coupe screens that are not mode 4.</summary>
public enum SamCoupeScreenMode {

  /// <summary>256x192, one bit per pixel over a ZX-Spectrum-compatible display file.</summary>
  Mode1 = 1,

  /// <summary>256x192, one bit per pixel with one attribute byte per scanline.</summary>
  Mode2 = 2,

  /// <summary>512x192 stored, two bits per pixel, drawn on 384 scanlines.</summary>
  Mode3 = 3,
}
