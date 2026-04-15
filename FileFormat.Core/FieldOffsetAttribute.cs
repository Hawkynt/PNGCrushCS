using System;

namespace FileFormat.Core;

/// <summary>Sets the cursor position to an absolute byte offset before reading/writing this field.
/// In sequential mode, this jumps the cursor to the specified position (like <c>System.Runtime.InteropServices.FieldOffsetAttribute</c> for binary serialization).
/// Use this for headers with gaps, non-contiguous fields, or union-like overlapping layouts.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class FieldOffsetAttribute(int offset) : Attribute {
  public int Offset { get; } = offset;
}
