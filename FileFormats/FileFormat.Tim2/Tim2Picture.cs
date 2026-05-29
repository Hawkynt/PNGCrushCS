namespace FileFormat.Tim2;

/// <summary>A single picture within a TIM2 file.</summary>
public sealed class Tim2Picture {
  public int Width { get; init; }
  public int Height { get; init; }
  public Tim2Format Format { get; init; }
  public byte MipmapCount { get; init; }

  /// <summary>Raw pixel data for this picture.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Palette data, null if the format is not indexed.</summary>
  public byte[]? PaletteData { get; init; }
  public int PaletteColors { get; init; }
}
