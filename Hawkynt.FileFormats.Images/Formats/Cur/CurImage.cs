using FileFormat.Ico;

namespace FileFormat.Cur;

/// <summary>A single cursor image entry with hotspot information.</summary>
public sealed class CurImage {
  public int Width { get; init; }
  public int Height { get; init; }
  public int BitsPerPixel { get; init; }
  public IcoImageFormat Format { get; init; }
  public byte[] Data { get; init; } = [];
  public ushort HotspotX { get; init; }
  public ushort HotspotY { get; init; }
}
