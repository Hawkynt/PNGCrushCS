using System;

namespace FileFormat.Core;

/// <summary>Declares a magic byte signature for automatic format detection.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class FormatMagicBytesAttribute(byte[] signature, int offset = 0) : Attribute {
  public byte[] Signature { get; } = signature;
  public int Offset { get; } = offset;
  public int MinHeaderLength => Offset + Signature.Length;
}
