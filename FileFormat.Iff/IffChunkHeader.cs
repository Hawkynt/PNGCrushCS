using FileFormat.Core;
using FileFormat.Riff;

namespace FileFormat.Iff;

/// <summary>The 8-byte IFF chunk header (ChunkId + Size, big-endian).</summary>
[GenerateSerializer, Endian(Endianness.Big)]
public readonly partial record struct IffChunkHeader( FourCC ChunkId, int Size
) {

 public const int StructSize = 8;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<IffChunkHeader>();
}
