using FileFormat.Core;

namespace FileFormat.Cmu;

/// <summary>The 8-byte header at the start of every CMU file: Width (int32 BE), Height (int32 BE).</summary>
[GenerateSerializer]
public readonly partial record struct CmuHeader(
  [property: HeaderField(0, 4, Endianness = Endianness.Big)] int Width,
  [property: HeaderField(4, 4, Endianness = Endianness.Big)] int Height
) {

  public const int StructSize = 8;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<CmuHeader>();
}
