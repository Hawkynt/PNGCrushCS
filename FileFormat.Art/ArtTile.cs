namespace FileFormat.Art;

/// <summary>A single tile within a Build Engine ART file.</summary>
public sealed class ArtTile {
  /// <summary>Width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Animation type.</summary>
  public ArtAnimType AnimType { get; init; }

  /// <summary>Number of animation frames (bits 0-5 of PicAnm).</summary>
  public int NumFrames { get; init; }

  /// <summary>X offset (bits 10-15 of PicAnm, sign-extended 6-bit).</summary>
  public int XOffset { get; init; }

  /// <summary>Y offset (bits 16-23 of PicAnm, sign-extended 8-bit).</summary>
  public int YOffset { get; init; }

  /// <summary>Animation speed (bits 24-31 of PicAnm).</summary>
  public int AnimSpeed { get; init; }

  /// <summary>Row-major 8-bit indexed pixel data (length = Width * Height).</summary>
  public byte[] PixelData { get; init; } = [];
}
