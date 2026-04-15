using System;

namespace FileFormat.Core;

/// <summary>Discriminated union: the type of this field depends on the value of another field.
/// Use with <see cref="CaseAttribute"/> to map discriminator values to concrete types.
/// <para>Example:</para>
/// <code>
/// [SwitchOn(nameof(Signature))]
/// [Case(0x04034B50u, typeof(ZipLocalFileHeader))]
/// [Case(0x02014B50u, typeof(ZipCentralDirEntry))]
/// object Body
/// </code></summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class SwitchOnAttribute(string fieldName) : Attribute {
  /// <summary>Name of the discriminator field.</summary>
  public string FieldName { get; } = fieldName;
}

/// <summary>Maps a discriminator value to a concrete type for <see cref="SwitchOnAttribute"/>.
/// The referenced type must have a <c>ReadFrom(ReadOnlySpan&lt;byte&gt;)</c> method (generated or hand-written).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = true)]
public sealed class CaseAttribute(object value, Type type) : Attribute {
  /// <summary>The discriminator value that selects this type.</summary>
  public object Value { get; } = value;
  /// <summary>The concrete type to parse when the discriminator matches.</summary>
  public Type Type { get; } = type;
}
