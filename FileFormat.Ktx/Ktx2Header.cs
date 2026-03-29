using FileFormat.Core;

namespace FileFormat.Ktx;

/// <summary>The 80-byte KTX2 header (always little-endian).</summary>
[GenerateSerializer]
[HeaderFiller("Identifier", 0, 12)]
public readonly partial record struct Ktx2Header(
  [property: HeaderField(12, 4)] int VkFormat,
  [property: HeaderField(16, 4)] int TypeSize,
  [property: HeaderField(20, 4)] int PixelWidth,
  [property: HeaderField(24, 4)] int PixelHeight,
  [property: HeaderField(28, 4)] int PixelDepth,
  [property: HeaderField(32, 4)] int LayerCount,
  [property: HeaderField(36, 4)] int FaceCount,
  [property: HeaderField(40, 4)] int LevelCount,
  [property: HeaderField(44, 4)] int SupercompressionScheme,
  [property: HeaderField(48, 4)] int DfdByteOffset,
  [property: HeaderField(52, 4)] int DfdByteLength,
  [property: HeaderField(56, 4)] int KvdByteOffset,
  [property: HeaderField(60, 4)] int KvdByteLength,
  [property: HeaderField(64, 8)] long SgdByteOffset,
  [property: HeaderField(72, 8)] long SgdByteLength
) {

  public const int StructSize = 80;
  public const int IdentifierSize = 12;

  public static readonly byte[] Identifier = { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A };

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<Ktx2Header>();
}
