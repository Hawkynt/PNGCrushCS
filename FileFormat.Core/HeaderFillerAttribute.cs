using System;

namespace FileFormat.Core;

/// <summary>Declares a padding or reserved region in the binary header that does not map to a property.</summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = true)]
public sealed class HeaderFillerAttribute(string name, int offset, int size) : Attribute {
  public string Name { get; } = name;
  public int Offset { get; } = offset;
  public int Size { get; } = size;
}
