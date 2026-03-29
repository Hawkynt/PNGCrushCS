namespace FileFormat.Art;

/// <summary>Animation type for a tile in a Build Engine ART file.</summary>
public enum ArtAnimType {
  /// <summary>No animation.</summary>
  None = 0,
  /// <summary>Oscillating (ping-pong) animation.</summary>
  Oscillate = 1,
  /// <summary>Forward animation.</summary>
  Forward = 2,
  /// <summary>Backward animation.</summary>
  Backward = 3,
}
