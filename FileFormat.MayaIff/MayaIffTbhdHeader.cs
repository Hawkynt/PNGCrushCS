using FileFormat.Core;

namespace FileFormat.MayaIff;

/// <summary>Maya IFF TBHD (tile-based header) chunk data -- 32 bytes, big-endian.</summary>
[GenerateSerializer, Endian(Endianness.Big)]
[Filler(24, 8)]
public readonly partial record struct MayaIffTbhdHeader( uint Width, uint Height, ushort Prnum, ushort Prden, uint Flags, ushort Bytes, ushort Tiles, uint Compression
) {

 public const int StructSize = 32;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<MayaIffTbhdHeader>();
}
