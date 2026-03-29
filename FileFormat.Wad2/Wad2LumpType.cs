namespace FileFormat.Wad2;

/// <summary>Types of lumps in a WAD2 file.</summary>
public enum Wad2LumpType : byte {
  /// <summary>Palette data.</summary>
  Palette = 0x40,
  /// <summary>Status bar picture.</summary>
  StatusBar = 0x42,
  /// <summary>Mip-mapped texture.</summary>
  MipTex = 0x44,
  /// <summary>Console picture.</summary>
  ConsolePic = 0x45,
}
