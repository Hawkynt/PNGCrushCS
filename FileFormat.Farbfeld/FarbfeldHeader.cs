using FileFormat.Core;

namespace FileFormat.Farbfeld;

/// <summary>The 16-byte header at the start of every Farbfeld file: magic "farbfeld" (8 bytes), width (uint32 BE), height (uint32 BE).</summary>
[GenerateSerializer, Endian(Endianness.Big)]
public readonly partial record struct FarbfeldHeader(
  byte Magic1,
  byte Magic2,
  byte Magic3,
  byte Magic4,
  byte Magic5,
  byte Magic6,
  byte Magic7,
  byte Magic8,
  int Width,
  int Height
) {

 public const int StructSize = 16;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<FarbfeldHeader>();
}
