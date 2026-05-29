namespace FileFormat.OpenRaster;

/// <summary>Represents a single layer in an OpenRaster image.</summary>
public sealed class OpenRasterLayer {
  /// <summary>Layer display name.</summary>
  public string Name { get; init; } = "";

  /// <summary>Horizontal offset of the layer within the canvas.</summary>
  public int X { get; init; }

  /// <summary>Vertical offset of the layer within the canvas.</summary>
  public int Y { get; init; }

  /// <summary>Layer width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Layer height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Layer opacity from 0.0 (transparent) to 1.0 (opaque).</summary>
  public float Opacity { get; init; } = 1.0f;

  /// <summary>Whether the layer is visible.</summary>
  public bool Visibility { get; init; } = true;

  /// <summary>Raw RGBA pixel data (4 bytes per pixel, row-major).</summary>
  public byte[] PixelData { get; init; } = [];
}
