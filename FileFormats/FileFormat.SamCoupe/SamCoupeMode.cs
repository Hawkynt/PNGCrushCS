namespace FileFormat.SamCoupe;

/// <summary>SAM Coupe display modes.</summary>
public enum SamCoupeMode {
  /// <summary>512x192, 2bpp packed (4 pixels per byte), 128 bytes/line.</summary>
  Mode3 = 3,
  /// <summary>256x192, 4bpp packed (2 pixels per byte, high nibble first), 128 bytes/line.</summary>
  Mode4 = 4
}
