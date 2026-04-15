using System;

namespace FileFormat.Core;

/// <summary>Overrides the wire format for a field. The C# property type stays the same, but the generator emits
/// conversion code between the wire representation and the property type.
/// <para>Example: <c>[TypeOverride(WireType.BCD)] int SerialNumber</c> — 4 bytes of BCD on disk, surfaces as int.</para>
/// <para>Example: <c>[TypeOverride(WireType.ZigZag)] int Delta</c> — protobuf-style zigzag encoding.</para></summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class TypeOverrideAttribute(WireType wireType) : Attribute {
  public WireType WireType { get; } = wireType;
}

/// <summary>Wire representation types for fields where the on-disk encoding differs from the C# type.</summary>
public enum WireType {
  /// <summary>Default: native binary representation matching the C# type.</summary>
  Native,

  // --- Integer encodings ---

  /// <summary>Binary-Coded Decimal. Each nibble encodes one decimal digit (0-9). 4 bytes = 8 digits.</summary>
  BCD,
  /// <summary>ZigZag encoding (protobuf-style): maps signed integers to unsigned ((n &lt;&lt; 1) ^ (n &gt;&gt; 31)).</summary>
  ZigZag,
  /// <summary>Gray code: reflected binary code where adjacent values differ by one bit.</summary>
  GrayCode,
  /// <summary>Variable-length quantity (VLQ): 7 bits per byte, MSB continuation bit. Used in MIDI, ASN.1.</summary>
  VLQ,
  /// <summary>LEB128 unsigned: variable-length little-endian base-128. Used in DWARF, WebAssembly.</summary>
  ULEB128,
  /// <summary>LEB128 signed: variable-length signed little-endian base-128.</summary>
  SLEB128,

  // --- Fixed-point ---

  /// <summary>Q15.16 signed fixed-point: 16-bit integer part + 16-bit fractional part → float/double property.</summary>
  Q15_16,
  /// <summary>Q1.15 signed fixed-point: 1-bit sign + 15-bit fraction → float/double property.</summary>
  Q1_15,
  /// <summary>Q8.8 unsigned fixed-point: 8-bit integer + 8-bit fraction → float/double property.</summary>
  Q8_8,
  /// <summary>Q16.16 unsigned fixed-point: 16-bit integer + 16-bit fraction → float/double property.</summary>
  UQ16_16,

  // --- Floating-point variants ---

  /// <summary>IEEE 754 half-precision (16-bit, 1+5+10). Maps to System.Half or float.</summary>
  Float16,
  /// <summary>Minifloat M4E3 (8-bit, 4-bit mantissa + 3-bit exponent + sign). Used in some GPU formats.</summary>
  M4E3,
  /// <summary>Minifloat M3E5 (8-bit, 3-bit mantissa + 5-bit exponent + sign). Used in some HDR formats.</summary>
  M3E5,
  /// <summary>Brain floating-point bfloat16 (16-bit, 1+8+7). Truncated float32 used in ML.</summary>
  BFloat16,

  // --- Wide integers ---

  /// <summary>Unsigned 24-bit integer (3 bytes). Maps to int/uint property.</summary>
  UInt24,
  /// <summary>Signed 24-bit integer (3 bytes, two's complement). Maps to int property.</summary>
  Int24,
  /// <summary>Unsigned 48-bit integer (6 bytes). Maps to long/ulong property.</summary>
  UInt48,
  /// <summary>96-bit integer (12 bytes). Maps to a custom Int96 type or decimal.</summary>
  Int96,
  /// <summary>Unsigned 128-bit integer (16 bytes). Maps to System.UInt128 (.NET 7+).</summary>
  UInt128,
  /// <summary>Signed 128-bit integer (16 bytes). Maps to System.Int128 (.NET 7+).</summary>
  Int128,

  // --- String-as-number ---

  /// <summary>Octal ASCII string: null-terminated octal digits → integer. Used in TAR headers.</summary>
  OctalString,
  /// <summary>Decimal ASCII string: fixed-width decimal digits → integer. Used in Scitex CT.</summary>
  DecimalString,
  /// <summary>Hex ASCII string: fixed-width hex digits → integer.</summary>
  HexString,
}
