namespace FileFormat.Nie;

/// <summary>NIE pixel configuration byte values.</summary>
public enum NiePixelConfig : byte {
  /// <summary>BGRA non-premultiplied 8-bit per channel.</summary>
  Bgra8 = 0x62, // 'b'
  /// <summary>BGRA premultiplied alpha 8-bit per channel.</summary>
  BgraPremul8 = 0x70, // 'p'
  /// <summary>BGRA non-premultiplied 16-bit per channel.</summary>
  Bgra16 = 0x42, // 'B'
  /// <summary>BGRA premultiplied alpha 16-bit per channel.</summary>
  BgraPremul16 = 0x50, // 'P'
}
