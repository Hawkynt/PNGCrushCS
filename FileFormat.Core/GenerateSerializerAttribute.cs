using System;

namespace FileFormat.Core;

/// <summary>Marks a struct for automatic source generation of <c>ReadFrom</c> and <c>WriteTo</c> methods based on field attributes.
/// The layout mode is auto-detected: <see cref="FieldAttribute"/> → fixed-layout (absolute offsets);
/// bare properties or <see cref="SeqFieldAttribute"/> → sequential (cursor-based, declaration order).
/// You can force a mode via the constructor, but auto-detection is preferred.</summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class GenerateSerializerAttribute : Attribute {

  /// <summary>Creates a serializer with auto-detected layout mode.</summary>
  public GenerateSerializerAttribute() { }

  /// <summary>Creates a serializer with an explicit layout mode override.</summary>
  public GenerateSerializerAttribute(LayoutMode mode) {
    Mode = mode;
    ModeExplicit = true;
  }

  /// <summary>The layout mode. Ignored when auto-detected.</summary>
  public LayoutMode Mode { get; }

  /// <summary>True if the mode was explicitly set via constructor.</summary>
  internal bool ModeExplicit { get; }

  /// <summary>Byte value used to fill gaps/padding in WriteTo. Default is 0x00.</summary>
  public byte FillByte { get; init; }
}

/// <summary>Controls how the source generator positions fields in the binary layout.</summary>
public enum LayoutMode {
  /// <summary>Each field has an explicit byte offset via <see cref="FieldAttribute"/>.</summary>
  Fixed = 0,
  /// <summary>Fields are read sequentially in declaration order. Size is inferred from the C# type. Supports variable-length headers.</summary>
  Sequential = 1,
}
