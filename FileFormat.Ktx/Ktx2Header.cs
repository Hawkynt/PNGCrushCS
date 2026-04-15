using FileFormat.Core;

namespace FileFormat.Ktx;

/// <summary>The 80-byte KTX2 header (always little-endian).</summary>
[GenerateSerializer]
[Filler(0, 12)]
public readonly partial record struct Ktx2Header(
 [property: FieldOffset(12)] int VkFormat,
 int TypeSize,
 int PixelWidth,
 int PixelHeight,
 int PixelDepth,
 int LayerCount,
 int FaceCount,
 int LevelCount,
 int SupercompressionScheme,
 int DfdByteOffset,
 int DfdByteLength,
 int KvdByteOffset,
 int KvdByteLength,
 long SgdByteOffset,
 long SgdByteLength
) {

 public const int StructSize = 80;
 public const int IdentifierSize = 12;

 public static readonly byte[] Identifier = { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A };

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<Ktx2Header>();
}
