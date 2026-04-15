using FileFormat.Core;

namespace FileFormat.Riff;

/// <summary>The 8-byte RIFF sub-chunk header (ChunkId + Size).</summary>
[GenerateSerializer]
public readonly partial record struct RiffChunkHeader(
  [property: SeqField(Size = 4)] FourCC ChunkId,
  uint Size
) {

  public const int StructSize = 8;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<RiffChunkHeader>();
}
