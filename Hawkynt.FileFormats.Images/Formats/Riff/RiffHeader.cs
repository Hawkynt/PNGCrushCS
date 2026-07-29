using FileFormat.Core;

namespace FileFormat.Riff;

/// <summary>The 12-byte top-level RIFF file header (ChunkId + Size + FormType).</summary>
[GenerateSerializer]
public readonly partial record struct RiffHeader(
  [property: SeqField(Size = 4)] FourCC ChunkId,
  uint Size,
  [property: SeqField(Size = 4)] FourCC FormType
) {

  public const int StructSize = 12;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<RiffHeader>();
}
