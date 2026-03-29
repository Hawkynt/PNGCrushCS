using System;

namespace FileFormat.Ico;

/// <summary>A single image entry in an ICO file.</summary>
public sealed class IcoImage {
  public int Width { get; init; }
  public int Height { get; init; }
  public int BitsPerPixel { get; init; }
  public IcoImageFormat Format { get; init; }
  public byte[] Data { get; init; } = [];
}
