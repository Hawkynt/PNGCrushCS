using System;

namespace FileFormat.Core;

/// <summary>The byte size of this field comes from another field's value. For sequential mode only.
/// <para>Example: <c>[SizedBy(nameof(NameLength))] byte[] Name</c> — reads NameLength bytes.</para>
/// <para>Example: <c>[SizedBy(nameof(ExtraLength), Prefix = true)] byte[] Extra</c> — reads a 2-byte length prefix, then that many bytes.</para></summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class SizedByAttribute(string fieldName) : Attribute {
  /// <summary>Name of the field whose value determines this field's byte size.</summary>
  public string FieldName { get; } = fieldName;

  /// <summary>When true, the size is a length-prefix immediately before the data (not a separate named field).
  /// The prefix size in bytes is specified by <see cref="PrefixSize"/> (default 2).</summary>
  public bool Prefix { get; init; }

  /// <summary>Byte size of the length prefix when <see cref="Prefix"/> is true. Default is 2 (uint16).</summary>
  public int PrefixSize { get; init; } = 2;
}
