using System;

namespace FileFormat.Core;

/// <summary>Declares the expected total byte size of a serialized header. The source generator emits a compile-time assertion
/// that the computed size from <see cref="FieldAttribute"/> annotations matches this value.</summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
public sealed class StructSizeAttribute(int size) : Attribute {
  public int Size { get; } = size;
}
