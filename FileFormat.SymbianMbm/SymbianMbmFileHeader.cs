using FileFormat.Core;

namespace FileFormat.SymbianMbm;

/// <summary>The 20-byte file header at the start of every MBM file: UID1, UID2, UID3, Checksum, TrailerOffset.</summary>
[GenerateSerializer]
public readonly partial record struct SymbianMbmFileHeader(
  [property: HeaderField(0, 4)] uint Uid1,
  [property: HeaderField(4, 4)] uint Uid2,
  [property: HeaderField(8, 4)] uint Uid3,
  [property: HeaderField(12, 4)] uint Checksum,
  [property: HeaderField(16, 4)] int TrailerOffset
) {

  public const int StructSize = 20;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<SymbianMbmFileHeader>();
}
