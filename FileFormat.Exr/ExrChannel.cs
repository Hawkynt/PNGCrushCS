namespace FileFormat.Exr;

/// <summary>Describes a single channel in an OpenEXR image.</summary>
public sealed class ExrChannel {
  public string Name { get; init; } = string.Empty;
  public ExrPixelType PixelType { get; init; }
  public int XSampling { get; init; } = 1;
  public int YSampling { get; init; } = 1;
}
