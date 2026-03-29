using FileFormat.Core;

namespace FileFormat.Riff;

/// <summary>The 8-byte RIFF sub-chunk header (ChunkId + Size).</summary>
[GenerateSerializer]
public readonly partial record struct RiffChunkHeader(
  [property: HeaderField(0, 4)] FourCC ChunkId,
  [property: HeaderField(4, 4)] uint Size
) {

  public const int StructSize = 8;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<RiffChunkHeader>();
}
