using FileFormat.Core;

namespace FileFormat.GemImg;

/// <summary>The 16-byte GEM IMG header at the start of every IMG file (big-endian).</summary>
[GenerateSerializer]
public readonly partial record struct GemImgHeader(
  [property: HeaderField(0, 2, Endianness = Endianness.Big)] short Version,
  [property: HeaderField(2, 2, Endianness = Endianness.Big)] short HeaderLength,
  [property: HeaderField(4, 2, Endianness = Endianness.Big)] short NumPlanes,
  [property: HeaderField(6, 2, Endianness = Endianness.Big)] short PatternLength,
  [property: HeaderField(8, 2, Endianness = Endianness.Big)] short PixelWidth,
  [property: HeaderField(10, 2, Endianness = Endianness.Big)] short PixelHeight,
  [property: HeaderField(12, 2, Endianness = Endianness.Big)] short ScanWidth,
  [property: HeaderField(14, 2, Endianness = Endianness.Big)] short ScanLines
) {

  public const int StructSize = 16;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<GemImgHeader>();
}
