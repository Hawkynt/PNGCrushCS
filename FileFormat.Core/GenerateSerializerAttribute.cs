using System;

namespace FileFormat.Core;

/// <summary>Marks a struct for automatic source generation of <c>ReadFrom</c> and <c>WriteTo</c> methods based on <see cref="HeaderFieldAttribute"/> metadata.</summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class GenerateSerializerAttribute : Attribute {
  /// <summary>Byte value used to fill gaps/padding in WriteTo. Default is 0x00.
  /// Set to 0x20 for space-filled formats like Scitex CT.</summary>
  public byte FillByte { get; init; }
}
