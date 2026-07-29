namespace FileFormat.Bsave;

/// <summary>IBM PC BSAVE graphics mode identifiers.</summary>
public enum BsaveMode {
  Cga320x200x4 = 0,
  Ega640x350x16 = 1,
  Vga320x200x256 = 2,
  Cga640x200x2 = 3,

  /// <summary>Unofficial CGA "160x100x16" tweak mode (40-column text mode with 2-line characters).
  /// Stored on-disk as 8000 bytes of nibble-packed 4-bit indices (16-colour RGBI palette).</summary>
  Cga160x100x16 = 4,

  /// <summary>Unofficial CGA "80x100x1024" mode (Reenigne / int10h.org). 80-column text mode with
  /// CRTC re-programmed for 2-scanline characters and four glyphs (0x55, 0x13, 0xB0, 0xB1) producing
  /// distinct NTSC phase patterns. Stored as 16000 bytes of standard text-mode (char, attr) cells —
  /// 1024 unique cell appearances from 4 chars × 16 fg × 16 bg.</summary>
  Cga80x100x1024 = 5,
}
