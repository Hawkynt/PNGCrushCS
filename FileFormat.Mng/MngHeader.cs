using FileFormat.Core;

namespace FileFormat.Mng;

/// <summary>MHDR chunk data (28 bytes) for MNG files.</summary>
[GenerateSerializer]
public readonly partial record struct MngHeader(
  [property: HeaderField(0, 4, Endianness = Endianness.Big)] uint Width,
  [property: HeaderField(4, 4, Endianness = Endianness.Big)] uint Height,
  [property: HeaderField(8, 4, Endianness = Endianness.Big)] uint TicksPerSecond,
  [property: HeaderField(12, 4, Endianness = Endianness.Big)] uint NominalLayerCount,
  [property: HeaderField(16, 4, Endianness = Endianness.Big)] uint NominalFrameCount,
  [property: HeaderField(20, 4, Endianness = Endianness.Big)] uint NominalPlayTime,
  [property: HeaderField(24, 4, Endianness = Endianness.Big)] uint Profile
) {
  public const int StructSize = 28;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<MngHeader>();
}
