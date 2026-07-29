using System.Text;
using FileFormat.Core;

namespace FileFormat.Png;

/// <summary>The 8-byte chunk header (length + type) preceding every PNG chunk.</summary>
[GenerateSerializer, Endian(Endianness.Big)]
public readonly partial record struct PngChunkHeader(
  int Length,
  byte TypeByte0,
  byte TypeByte1,
  byte TypeByte2,
  byte TypeByte3
) {

 public const int StructSize = 8;

 /// <summary>The 4-character ASCII chunk type.</summary>
 public string Type => Encoding.ASCII.GetString([this.TypeByte0, this.TypeByte1, this.TypeByte2, this.TypeByte3]);

 /// <summary>Create a chunk header from a length and a 4-character type string.</summary>
 public static PngChunkHeader Create(int length, string type) => new(length, (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3]);

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<PngChunkHeader>();
}
