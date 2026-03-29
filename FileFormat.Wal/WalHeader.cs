using FileFormat.Core;

namespace FileFormat.Wal;

/// <summary>The 100-byte WAL header for Quake 2 texture files.</summary>
[GenerateSerializer]
public readonly partial record struct WalHeader(
  [property: HeaderField(0, 32)] string Name,
  [property: HeaderField(32, 4)] uint Width,
  [property: HeaderField(36, 4)] uint Height,
  [property: HeaderField(40, 4)] uint MipOffset0,
  [property: HeaderField(44, 4)] uint MipOffset1,
  [property: HeaderField(48, 4)] uint MipOffset2,
  [property: HeaderField(52, 4)] uint MipOffset3,
  [property: HeaderField(56, 32)] string NextFrameName,
  [property: HeaderField(88, 4)] uint Flags,
  [property: HeaderField(92, 4)] uint Contents,
  [property: HeaderField(96, 4)] uint Value
) {

  public const int StructSize = 100;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<WalHeader>();
}
