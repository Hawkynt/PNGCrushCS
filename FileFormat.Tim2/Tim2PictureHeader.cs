using FileFormat.Core;

namespace FileFormat.Tim2;

/// <summary>The 48-byte per-picture header in a TIM2 file.</summary>
[GenerateSerializer]
public readonly partial record struct Tim2PictureHeader(
  uint TotalSize,
  uint PaletteSize,
  uint ImageDataSize,
  ushort HeaderSize,
  ushort PaletteColors,
  byte PictureFormat,
  byte Mipmaps,
  byte PaletteType,
  byte ImageType,
  ushort Width,
  ushort Height,
  ulong GsTex0,
  ulong GsTex1,
  uint GsFlags,
  uint GsTexClut
) {

 public const int StructSize = 48;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<Tim2PictureHeader>();
}
