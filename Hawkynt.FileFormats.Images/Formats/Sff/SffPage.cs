namespace FileFormat.Sff;

/// <summary>A single page in a SFF (Structured Fax File).</summary>
public sealed class SffPage {

  /// <summary>Width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Height in scanlines.</summary>
  public int Height { get; init; }

  /// <summary>Horizontal resolution code.</summary>
  public byte HResolution { get; init; }

  /// <summary>Vertical resolution code.</summary>
  public byte VResolution { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; } = [];
}
