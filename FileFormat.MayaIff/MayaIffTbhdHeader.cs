using FileFormat.Core;

namespace FileFormat.MayaIff;

/// <summary>Maya IFF TBHD (tile-based header) chunk data -- 32 bytes, big-endian.</summary>
[GenerateSerializer]
[HeaderFiller("Reserved", 24, 8)]
public readonly partial record struct MayaIffTbhdHeader(
  [property: HeaderField(0, 4, Endianness = Endianness.Big)] uint Width,
  [property: HeaderField(4, 4, Endianness = Endianness.Big)] uint Height,
  [property: HeaderField(8, 2, Endianness = Endianness.Big)] ushort Prnum,
  [property: HeaderField(10, 2, Endianness = Endianness.Big)] ushort Prden,
  [property: HeaderField(12, 4, Endianness = Endianness.Big)] uint Flags,
  [property: HeaderField(16, 2, Endianness = Endianness.Big)] ushort Bytes,
  [property: HeaderField(18, 2, Endianness = Endianness.Big)] ushort Tiles,
  [property: HeaderField(20, 4, Endianness = Endianness.Big)] uint Compression
) {

  public const int StructSize = 32;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<MayaIffTbhdHeader>();
}
