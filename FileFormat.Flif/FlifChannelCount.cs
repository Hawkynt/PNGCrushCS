namespace FileFormat.Flif;

/// <summary>Channel count as encoded in the FLIF header byte (bits 0-2).</summary>
public enum FlifChannelCount : byte {
  /// <summary>Grayscale (1 channel).</summary>
  Gray = 1,
  /// <summary>RGB (3 channels).</summary>
  Rgb = 3,
  /// <summary>RGBA (4 channels).</summary>
  Rgba = 4,
}
