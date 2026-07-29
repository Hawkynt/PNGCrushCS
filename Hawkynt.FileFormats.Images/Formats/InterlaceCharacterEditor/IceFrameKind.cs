namespace FileFormat.InterlaceCharacterEditor;

/// <summary>How one of the two interlaced frames turns font bytes into colours.</summary>
public enum IceFrameKind {

  /// <summary>ANTIC mode 4: two bits per pixel choosing among four registers, four pixels per byte.</summary>
  Graphics12,

  /// <summary>GTIA 9: a nibble per pixel giving the luminance of the background hue, two per byte.</summary>
  Gtia9,

  /// <summary>GTIA 10: a nibble per pixel indexing the colour registers directly.</summary>
  Gtia10,

  /// <summary>GTIA 11: a nibble per pixel giving the hue at the background luminance.</summary>
  Gtia11,
}
