namespace FileFormat.Tiny;

/// <summary>Atari ST display resolution encoded by a Tiny Stuff picture.</summary>
public enum TinyResolution {
  /// <summary>320x200 pixels using four interleaved bitplanes and up to 16 colours.</summary>
  Low = 0,

  /// <summary>640x200 pixels using two interleaved bitplanes and up to four colours.</summary>
  Medium = 1,

  /// <summary>640x400 pixels using the Atari ST monochrome one-plane display.</summary>
  High = 2,
}
