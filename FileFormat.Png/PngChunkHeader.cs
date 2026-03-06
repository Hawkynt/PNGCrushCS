using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Png;

/// <summary>The 8-byte chunk header (length + type) preceding every PNG chunk.</summary>
public readonly record struct PngChunkHeader(int Length, byte TypeByte0, byte TypeByte1, byte TypeByte2, byte TypeByte3) {

  public const int StructSize = 8;

  /// <summary>The 4-character ASCII chunk type.</summary>
  public string Type => Encoding.ASCII.GetString([this.TypeByte0, this.TypeByte1, this.TypeByte2, this.TypeByte3]);

  /// <summary>Create a chunk header from a length and a 4-character type string.</summary>
  public static PngChunkHeader Create(int length, string type) => new(length, (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3]);

  public static PngChunkHeader ReadFrom(ReadOnlySpan<byte> source) => new(
    BinaryPrimitives.ReadInt32BigEndian(source),
    source[4],
    source[5],
    source[6],
    source[7]
  );

  public void WriteTo(Span<byte> destination) {
    BinaryPrimitives.WriteInt32BigEndian(destination, this.Length);
    destination[4] = this.TypeByte0;
    destination[5] = this.TypeByte1;
    destination[6] = this.TypeByte2;
    destination[7] = this.TypeByte3;
  }

  public static HeaderFieldDescriptor[] GetFieldMap() => [
    new("Length", 0, 4),
    new("TypeByte0", 4, 1),
    new("TypeByte1", 5, 1),
    new("TypeByte2", 6, 1),
    new("TypeByte3", 7, 1)
  ];
}
