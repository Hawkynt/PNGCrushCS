using FileFormat.Core;

namespace FileFormat.Farbfeld;

/// <summary>The 16-byte header at the start of every Farbfeld file: magic "farbfeld" (8 bytes), width (uint32 BE), height (uint32 BE).</summary>
[GenerateSerializer]
public readonly partial record struct FarbfeldHeader(
  [property: HeaderField(0, 1)] byte Magic1,
  [property: HeaderField(1, 1)] byte Magic2,
  [property: HeaderField(2, 1)] byte Magic3,
  [property: HeaderField(3, 1)] byte Magic4,
  [property: HeaderField(4, 1)] byte Magic5,
  [property: HeaderField(5, 1)] byte Magic6,
  [property: HeaderField(6, 1)] byte Magic7,
  [property: HeaderField(7, 1)] byte Magic8,
  [property: HeaderField(8, 4, Endianness = Endianness.Big)] int Width,
  [property: HeaderField(12, 4, Endianness = Endianness.Big)] int Height
) {

  public const int StructSize = 16;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<FarbfeldHeader>();
}
