namespace FileFormat.DaliCompressed;

/// <summary>Atari ST screen resolution a compressed Dali file holds.</summary>
public enum DaliResolution {
  /// <summary>320x200 in 16 colours (.lpk).</summary>
  Low = 0,

  /// <summary>640x200 in 4 colours (.mpk).</summary>
  Medium = 1,

  /// <summary>640x400 monochrome (.hpk).</summary>
  High = 2,
}
