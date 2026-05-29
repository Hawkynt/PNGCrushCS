namespace FileFormat.Exr;

/// <summary>A generic name-typed attribute stored in an OpenEXR header.</summary>
public sealed class ExrAttribute {
  public string Name { get; init; } = string.Empty;
  public string TypeName { get; init; } = string.Empty;
  public byte[] Value { get; init; } = [];
}
