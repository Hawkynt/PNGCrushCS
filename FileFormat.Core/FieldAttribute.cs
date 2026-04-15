using System;

namespace FileFormat.Core;

/// <summary>Marks a property with its byte offset and size in the binary header. Marks a property with its byte offset and size for fixed-layout headers.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class FieldAttribute(int offset, int size) : Attribute {
  public int Offset { get; } = offset;
  public int Size { get; } = size;

  /// <summary>Override the property name in the field map.</summary>
  public string? Name { get; init; }

  /// <summary>The byte order for multi-byte fields. When not set, uses the type-level <see cref="EndianAttribute"/> default, falling back to <see cref="Endianness.Little"/>.</summary>
  public Endianness Endianness { get; init; } = (Endianness)(-1); // sentinel: not explicitly set

  /// <summary>Name of a field that determines endianness at runtime (for formats like TIFF II/MM).</summary>
  public string? EndianFieldName { get; init; }

  /// <summary>When set together with <see cref="EndianFieldName"/>, the value that indicates big-endian byte order.</summary>
  public int EndianComputeValue { get; init; } = int.MinValue;

  /// <summary>For fixed-size array fields, the number of elements.</summary>
  public int ArrayLength { get; init; }

  /// <summary>For bitfield extraction, the starting bit position within the field. -1 means no bitfield.</summary>
  public int BitOffset { get; init; } = -1;

  /// <summary>For bitfield extraction, the number of bits to extract.</summary>
  public int BitCount { get; init; }

}
