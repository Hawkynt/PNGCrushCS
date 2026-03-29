using FileFormat.Core;

namespace FileFormat.CokeAtari;

/// <summary>The 4-byte header of a COKE file: Width (ushort BE), Height (ushort BE).</summary>
[GenerateSerializer]
public readonly partial record struct CokeAtariHeader(
  [property: HeaderField(0, 2, Endianness = Endianness.Big)] ushort Width,
  [property: HeaderField(2, 2, Endianness = Endianness.Big)] ushort Height
) {

  public const int StructSize = 4;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<CokeAtariHeader>();
}
