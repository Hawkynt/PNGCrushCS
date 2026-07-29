namespace FileFormat.MsxGl16;

/// <summary>The two screens a sixteen-colour GL picture can belong to.</summary>
public enum MsxGl16Mode {

  /// <summary>Screen 5: 256 pixels across, drawn one scanline per stored row.</summary>
  Screen5,

  /// <summary>Screen 7: 512 pixels across, drawn two scanlines per stored row.</summary>
  Screen7,
}
