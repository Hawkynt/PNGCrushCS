using FileFormat.Core;

namespace FileFormat.Gd2;

[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct Gd2Header(
  [property: FieldOffset(4)] ushort Version,
  ushort Width,
  ushort Height,
  ushort ChunkSize,
  ushort Format,
  ushort XChunkCount,
  ushort YChunkCount
) {
  public const int StructSize = 18;
}
