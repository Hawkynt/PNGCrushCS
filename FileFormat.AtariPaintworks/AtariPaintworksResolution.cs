namespace FileFormat.AtariPaintworks;

/// <summary>Atari ST screen resolution modes used by Paintworks/GFA/DeskPic formats.</summary>
public enum AtariPaintworksResolution {
  /// <summary>Low resolution: 320x200, 4 bitplanes, 16 colors.</summary>
  Low = 0,
  /// <summary>Medium resolution: 640x200, 2 bitplanes, 4 colors.</summary>
  Medium = 1,
  /// <summary>High resolution: 640x400, 1 bitplane, monochrome.</summary>
  High = 2
}
