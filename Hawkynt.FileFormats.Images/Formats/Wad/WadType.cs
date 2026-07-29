namespace FileFormat.Wad;

/// <summary>The type of a WAD archive.</summary>
public enum WadType {
  /// <summary>Internal WAD (main game data).</summary>
  Iwad = 0,
  /// <summary>Patch WAD (mod/add-on data).</summary>
  Pwad = 1,
}
