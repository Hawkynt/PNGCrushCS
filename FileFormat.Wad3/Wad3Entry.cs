using FileFormat.Core;

namespace FileFormat.Wad3;

/// <summary>A 32-byte directory entry within a WAD3 file.</summary>
[GenerateSerializer]
public readonly partial record struct Wad3Entry(
  [property: HeaderField(0, 4)] int FilePos,
  [property: HeaderField(4, 4)] int DiskSize,
  [property: HeaderField(8, 4)] int Size,
  [property: HeaderField(12, 1)] byte Type,
  [property: HeaderField(13, 1)] byte Compression,
  [property: HeaderField(14, 2)] ushort Padding,
  [property: HeaderField(16, 16)] string Name
) {

  public const int StructSize = 32;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<Wad3Entry>();
}
