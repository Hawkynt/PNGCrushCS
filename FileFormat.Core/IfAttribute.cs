using System;

namespace FileFormat.Core;

/// <summary>Conditionally includes a field in sequential parsing. The field is only read/written when the referenced field matches the condition.
/// <para>Example: <c>[If(nameof(Flags), Op.HasFlag, MyFlags.HasExtra)]</c> — only parse this field if Flags has the HasExtra bit set.</para>
/// <para>Example: <c>[If(nameof(Version), Op.GreaterOrEqual, 2)]</c> — only parse this field for version 2+.</para>
/// When the condition is false, nullable fields get <c>null</c>; value types get <c>default</c>.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class IfAttribute(string fieldName, Op op, object value) : Attribute {
  /// <summary>Name of the field to test (must appear before this field in declaration order).</summary>
  public string FieldName { get; } = fieldName;
  /// <summary>Comparison operator.</summary>
  public Op Op { get; } = op;
  /// <summary>Value to compare against.</summary>
  public object Value { get; } = value;
}

/// <summary>Comparison operators for <see cref="IfAttribute"/>.</summary>
public enum Op {
  /// <summary>field == value</summary>
  Equal,
  /// <summary>field != value</summary>
  NotEqual,
  /// <summary>field &gt; value</summary>
  Greater,
  /// <summary>field &gt;= value</summary>
  GreaterOrEqual,
  /// <summary>field &lt; value</summary>
  Less,
  /// <summary>field &lt;= value</summary>
  LessOrEqual,
  /// <summary>(field &amp; value) != 0 — for [Flags] enums.</summary>
  HasFlag,
}
