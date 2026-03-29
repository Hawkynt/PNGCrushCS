using FileFormat.Core;

namespace FileFormat.Pkm;

/// <summary>The 16-byte header at the start of every PKM file.</summary>
[GenerateSerializer]
internal readonly partial record struct PkmHeader(
  [property: HeaderField(0, 1)] byte Magic1,
  [property: HeaderField(1, 1)] byte Magic2,
  [property: HeaderField(2, 1)] byte Magic3,
  [property: HeaderField(3, 1)] byte Magic4,
  [property: HeaderField(4, 1)] byte Version1,
  [property: HeaderField(5, 1)] byte Version2,
  [property: HeaderField(6, 2, Endianness = Endianness.Big)] ushort Format,
  [property: HeaderField(8, 2, Endianness = Endianness.Big)] ushort PaddedWidth,
  [property: HeaderField(10, 2, Endianness = Endianness.Big)] ushort PaddedHeight,
  [property: HeaderField(12, 2, Endianness = Endianness.Big)] ushort Width,
  [property: HeaderField(14, 2, Endianness = Endianness.Big)] ushort Height
) {

  public const int StructSize = 16;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<PkmHeader>();
}
