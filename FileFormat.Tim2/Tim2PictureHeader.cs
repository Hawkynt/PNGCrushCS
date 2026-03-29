using FileFormat.Core;

namespace FileFormat.Tim2;

/// <summary>The 48-byte per-picture header in a TIM2 file.</summary>
[GenerateSerializer]
public readonly partial record struct Tim2PictureHeader(
  [property: HeaderField(0, 4)] uint TotalSize,
  [property: HeaderField(4, 4)] uint PaletteSize,
  [property: HeaderField(8, 4)] uint ImageDataSize,
  [property: HeaderField(12, 2)] ushort HeaderSize,
  [property: HeaderField(14, 2)] ushort PaletteColors,
  [property: HeaderField(16, 1)] byte PictureFormat,
  [property: HeaderField(17, 1)] byte Mipmaps,
  [property: HeaderField(18, 1)] byte PaletteType,
  [property: HeaderField(19, 1)] byte ImageType,
  [property: HeaderField(20, 2)] ushort Width,
  [property: HeaderField(22, 2)] ushort Height,
  [property: HeaderField(24, 8)] ulong GsTex0,
  [property: HeaderField(32, 8)] ulong GsTex1,
  [property: HeaderField(40, 4)] uint GsFlags,
  [property: HeaderField(44, 4)] uint GsTexClut
) {

  public const int StructSize = 48;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<Tim2PictureHeader>();
}
