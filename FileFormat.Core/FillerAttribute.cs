using System;

namespace FileFormat.Core;

/// <summary>Declares a padding, reserved, or grouped region in the binary header.
/// Without a name, the region is anonymous padding reported as <c>"(padding)"</c>.
/// With a name, the region is treated as a named composite field spanning
/// <paramref name="size"/> bytes starting at <paramref name="offset"/>.</summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = true)]
public sealed class FillerAttribute(int offset, int size, string? name = null) : Attribute {
  public int Offset { get; } = offset;
  public int Size { get; } = size;
  public string? Name { get; } = name;
}
