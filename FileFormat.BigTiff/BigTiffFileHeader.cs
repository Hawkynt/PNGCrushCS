using FileFormat.Core;

namespace FileFormat.BigTiff;

/// <summary>BigTIFF 16-byte file header (LE-only serialization).</summary>
[GenerateSerializer]
public readonly partial record struct BigTiffFileHeader(
  [property: HeaderField(0, 2)] ushort ByteOrder,
  [property: HeaderField(2, 2)] ushort Version,
  [property: HeaderField(4, 2)] ushort OffsetSize,
  [property: HeaderField(6, 2)] ushort Reserved,
  [property: HeaderField(8, 8)] long FirstIfdOffset
) {

  public const int StructSize = 16;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<BigTiffFileHeader>();
}
