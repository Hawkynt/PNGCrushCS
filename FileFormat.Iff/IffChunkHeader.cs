using FileFormat.Core;
using FileFormat.Riff;

namespace FileFormat.Iff;

/// <summary>The 8-byte IFF chunk header (ChunkId + Size, big-endian).</summary>
[GenerateSerializer]
public readonly partial record struct IffChunkHeader(
  [property: HeaderField(0, 4)] FourCC ChunkId,
  [property: HeaderField(4, 4, Endianness = Endianness.Big)] int Size
) {

  public const int StructSize = 8;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<IffChunkHeader>();
}
