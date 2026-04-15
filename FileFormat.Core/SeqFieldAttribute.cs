using System;

namespace FileFormat.Core;

/// <summary>Marks a property as a sequentially-positioned field in a <see cref="LayoutMode.Sequential"/> header.
/// Fields are read in declaration order using a cursor. Size is inferred from the property type unless explicitly set.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class SeqFieldAttribute : Attribute {
  /// <summary>Explicit size in bytes. When 0, the size is inferred from the property type (1 for byte, 2 for short/ushort, 4 for int/uint/float, 8 for long/ulong/double).</summary>
  public int Size { get; init; }

  /// <summary>The byte order for this field. When not set, uses the type-level <see cref="EndianAttribute"/> default.</summary>
  public Endianness Endianness { get; init; } = (Endianness)(-1); // sentinel: not explicitly set
}
