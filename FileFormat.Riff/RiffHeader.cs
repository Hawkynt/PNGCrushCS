using FileFormat.Core;

namespace FileFormat.Riff;

/// <summary>The 12-byte top-level RIFF file header (ChunkId + Size + FormType).</summary>
[GenerateSerializer]
public readonly partial record struct RiffHeader(
  [property: HeaderField(0, 4)] FourCC ChunkId,
  [property: HeaderField(4, 4)] uint Size,
  [property: HeaderField(8, 4)] FourCC FormType
) {

  public const int StructSize = 12;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<RiffHeader>();
}
