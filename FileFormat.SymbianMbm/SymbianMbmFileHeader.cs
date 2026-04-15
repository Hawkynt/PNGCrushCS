using FileFormat.Core;

namespace FileFormat.SymbianMbm;

/// <summary>The 20-byte file header at the start of every MBM file: UID1, UID2, UID3, Checksum, TrailerOffset.</summary>
[GenerateSerializer]
public readonly partial record struct SymbianMbmFileHeader(
  uint Uid1,
  uint Uid2,
  uint Uid3,
  uint Checksum,
  int TrailerOffset
) {

 public const int StructSize = 20;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<SymbianMbmFileHeader>();
}
