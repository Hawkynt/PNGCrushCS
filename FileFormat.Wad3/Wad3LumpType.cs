namespace FileFormat.Wad3;

/// <summary>Types of lumps in a WAD3 file.</summary>
public enum Wad3LumpType : byte {
  /// <summary>Status bar picture.</summary>
  StatusBar = 0x42,
  /// <summary>Mip-mapped texture.</summary>
  MipTex = 0x43,
  /// <summary>Font data.</summary>
  Font = 0x45,
}
