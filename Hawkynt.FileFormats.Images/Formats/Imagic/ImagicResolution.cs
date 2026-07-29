namespace FileFormat.Imagic;

/// <summary>The Atari ST screen resolution an Imagic file was saved in.</summary>
public enum ImagicResolution {

  /// <summary>320x200 in sixteen colours, four bitplanes.</summary>
  Low = 0,

  /// <summary>640x200 in four colours, two bitplanes.</summary>
  Medium = 1,

  /// <summary>640x400 monochrome, one bitplane.</summary>
  High = 2,
}
