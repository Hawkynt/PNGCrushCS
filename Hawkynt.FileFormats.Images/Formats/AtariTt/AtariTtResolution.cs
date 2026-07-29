namespace FileFormat.AtariTt;

/// <summary>The Atari TT screen resolution a picture was saved in.</summary>
/// <remarks>The values are the mode numbers stored in the file header, not a running index.</remarks>
public enum AtariTtResolution {

  /// <summary>TT Medium: 640x480 in sixteen colours, four bitplanes.</summary>
  Medium = 4,

  /// <summary>TT High: 1280x960 monochrome, one bitplane.</summary>
  High = 6,

  /// <summary>TT Low: 320x480 in 256 colours, eight bitplanes, shown across 640 pixels.</summary>
  Low = 7,
}
