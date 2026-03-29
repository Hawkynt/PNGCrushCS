namespace FileFormat.AppleII;

/// <summary>Apple II graphics modes.</summary>
public enum AppleIIMode {
  /// <summary>Hi-Res Graphics: 280x192, 8192 bytes.</summary>
  Hgr = 0,
  /// <summary>Double Hi-Res Graphics: 560x192, 16384 bytes (8192 main + 8192 aux).</summary>
  Dhgr = 1
}
