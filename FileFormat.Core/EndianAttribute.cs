using System;

namespace FileFormat.Core;

/// <summary>Declares the default byte order for all fields in a <see cref="GenerateSerializerAttribute"/>-annotated type.
/// Individual fields can override this via <see cref="FieldAttribute.Endianness"/> .</summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
public sealed class EndianAttribute(Endianness endianness) : Attribute {
  public Endianness Endianness { get; } = endianness;
}
