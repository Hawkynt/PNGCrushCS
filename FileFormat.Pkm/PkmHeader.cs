using FileFormat.Core;

namespace FileFormat.Pkm;

/// <summary>The 16-byte header at the start of every PKM file.</summary>
[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct PkmHeader(
  byte Magic1,
  byte Magic2,
  byte Magic3,
  byte Magic4,
  byte Version1,
  byte Version2,
  ushort Format,
  ushort PaddedWidth,
  ushort PaddedHeight,
  ushort Width,
  ushort Height
) {

 public const int StructSize = 16;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<PkmHeader>();
}
