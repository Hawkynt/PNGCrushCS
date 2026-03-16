using System;

namespace FileFormat.Core;

/// <summary>Marks a primary constructor parameter (propagated to property) with its byte offset and size in the binary header.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class HeaderFieldAttribute(int offset, int size) : Attribute {
  public int Offset { get; } = offset;
  public int Size { get; } = size;

  /// <summary>Override the property name in the field map. Useful for computed or array-like fields.</summary>
  public string? Name { get; init; }

  /// <summary>The byte order for multi-byte fields. Defaults to <see cref="Endianness.Little"/>.</summary>
  public Endianness Endianness { get; init; } = Endianness.Little;

  /// <summary>Name of a bool parameter/property that determines endianness at runtime (for formats like DPX, KTX).</summary>
  public string? EndianFieldName { get; init; }

  /// <summary>When set together with <see cref="EndianFieldName"/>, the value of the referenced field that indicates big-endian byte order. Use <see cref="int.MinValue"/> (default) to indicate not set.</summary>
  public int EndianComputeValue { get; init; } = int.MinValue;

  /// <summary>For fixed-size array fields, the number of elements. Each element has size <c>Size / ArrayLength</c> bytes.</summary>
  public int ArrayLength { get; init; }

  /// <summary>For bitfield extraction, the starting bit position within the field. -1 means no bitfield.</summary>
  public int BitOffset { get; init; } = -1;

  /// <summary>For bitfield extraction, the number of bits to extract.</summary>
  public int BitCount { get; init; }

  /// <summary>When set to <see cref="AsciiEncoding.Decimal"/>, the field is stored as a
  /// fixed-width ASCII decimal string (right-aligned, zero-padded) instead of binary.</summary>
  public AsciiEncoding AsciiEncoding { get; init; }
}
