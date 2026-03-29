using FileFormat.Core;

namespace FileFormat.AtariFalconXga;

/// <summary>The 4-byte header of an Atari Falcon XGA file: Width (ushort BE), Height (ushort BE).</summary>
[GenerateSerializer]
public readonly partial record struct AtariFalconXgaHeader(
  [property: HeaderField(0, 2, Endianness = Endianness.Big)] ushort Width,
  [property: HeaderField(2, 2, Endianness = Endianness.Big)] ushort Height
) {

  public const int StructSize = 4;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<AtariFalconXgaHeader>();
}
