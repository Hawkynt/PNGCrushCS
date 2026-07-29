using FileFormat.Core;

namespace FileFormat.BigTiff;

/// <summary>BigTIFF 16-byte file header (LE-only serialization).</summary>
[GenerateSerializer]
public readonly partial record struct BigTiffFileHeader(
  ushort ByteOrder,
  ushort Version,
  ushort OffsetSize,
  ushort Reserved,
  long FirstIfdOffset
) {

 public const int StructSize = 16;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<BigTiffFileHeader>();
}
