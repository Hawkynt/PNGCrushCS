using System;
using FileFormat.Core;

namespace FileFormat.Png;

/// <summary>The 8-byte PNG signature at the start of every PNG file.</summary>
public readonly record struct PngSignatureHeader(byte Byte0, byte Byte1, byte Byte2, byte Byte3, byte Byte4, byte Byte5, byte Byte6, byte Byte7) {

  public const int StructSize = 8;

  /// <summary>The valid PNG signature: 137 80 78 71 13 10 26 10.</summary>
  public static PngSignatureHeader Expected => new(137, 80, 78, 71, 13, 10, 26, 10);

  /// <summary>Whether this signature matches the expected PNG signature.</summary>
  public bool IsValid => this == Expected;

  public static PngSignatureHeader ReadFrom(ReadOnlySpan<byte> source) => new(
    source[0],
    source[1],
    source[2],
    source[3],
    source[4],
    source[5],
    source[6],
    source[7]
  );

  public void WriteTo(Span<byte> destination) {
    destination[0] = this.Byte0;
    destination[1] = this.Byte1;
    destination[2] = this.Byte2;
    destination[3] = this.Byte3;
    destination[4] = this.Byte4;
    destination[5] = this.Byte5;
    destination[6] = this.Byte6;
    destination[7] = this.Byte7;
  }

  public static HeaderFieldDescriptor[] GetFieldMap() => [
    new("Byte0", 0, 1),
    new("Byte1", 1, 1),
    new("Byte2", 2, 1),
    new("Byte3", 3, 1),
    new("Byte4", 4, 1),
    new("Byte5", 5, 1),
    new("Byte6", 6, 1),
    new("Byte7", 7, 1)
  ];
}
