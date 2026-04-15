using System;

namespace FileFormat.Core;

/// <summary>Marks a <c>string</c> or <c>byte[]</c> field as a fixed-size encoded string.
/// Size comes from the field's <see cref="SeqFieldAttribute.Size"/>, <see cref="FieldAttribute.Size"/>, or type inference.
/// The string is read from exactly that many bytes and trimmed of trailing nulls.
/// <para>Character width is determined by encoding: ASCII/UTF-8/Latin1 = 1 byte per char, Unicode (UTF-16) = 2 bytes per char.</para></summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class StringAttribute : Attribute {

  /// <summary>Fixed-size string with ASCII encoding (default).</summary>
  public StringAttribute() => Encoding = StringEncoding.Ascii;

  /// <summary>Fixed-size string with the specified encoding.</summary>
  public StringAttribute(StringEncoding encoding) => Encoding = encoding;

  public StringEncoding Encoding { get; }

  /// <summary>Windows codepage number for <see cref="StringEncoding.Custom"/>. E.g., 437 for DOS CP437, 500 for EBCDIC International.</summary>
  public int CodePage { get; init; }
}

/// <summary>Marks a <c>string</c> field as a variable-length null-terminated string.
/// The string is read until the null terminator is found; the terminator is consumed but not included in the value.
/// <para>For multi-byte encodings (UTF-16), the terminator is 2 null bytes.</para></summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class StringZAttribute : Attribute {

  /// <summary>Null-terminated string with ASCII encoding (default).</summary>
  public StringZAttribute() => Encoding = StringEncoding.Ascii;

  /// <summary>Null-terminated string with the specified encoding.</summary>
  public StringZAttribute(StringEncoding encoding) => Encoding = encoding;

  public StringEncoding Encoding { get; }

  /// <summary>Windows codepage number for <see cref="StringEncoding.Custom"/>.</summary>
  public int CodePage { get; init; }
}

/// <summary>Text encoding for string fields in binary headers.</summary>
public enum StringEncoding {
  /// <summary>ASCII (7-bit, 1 byte per char).</summary>
  Ascii,
  /// <summary>UTF-8 (variable width, 1-4 bytes per char).</summary>
  Utf8,
  /// <summary>Latin1 / ISO-8859-1 (1 byte per char, full 0x00-0xFF range).</summary>
  Latin1,
  /// <summary>UTF-16 Little Endian (2 bytes per char).</summary>
  Unicode,
  /// <summary>UTF-16 Big Endian (2 bytes per char).</summary>
  UnicodeBE,
  /// <summary>EBCDIC (IBM mainframe, 1 byte per char, codepage 037).</summary>
  Ebcdic,
  /// <summary>PETSCII (Commodore 8-bit, 1 byte per char).</summary>
  Petscii,
  /// <summary>ATASCII (Atari 8-bit, 1 byte per char).</summary>
  Atascii,
  /// <summary>Custom codepage via <see cref="System.Text.Encoding.GetEncoding(int)"/>. Set CodePage on the attribute.</summary>
  Custom,
}
