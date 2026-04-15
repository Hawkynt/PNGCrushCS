using FileFormat.Core;

namespace FileFormat.Wal;

/// <summary>The 100-byte WAL header for Quake 2 texture files.</summary>
[GenerateSerializer]
public readonly partial record struct WalHeader(
  string Name,
  uint Width,
  uint Height,
  uint MipOffset0,
  uint MipOffset1,
  uint MipOffset2,
  uint MipOffset3,
  string NextFrameName,
  uint Flags,
  uint Contents,
  uint Value
) {

 public const int StructSize = 100;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<WalHeader>();
}
