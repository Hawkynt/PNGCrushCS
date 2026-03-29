using FileFormat.Core;

namespace FileFormat.Pvr;

/// <summary>The 52-byte PVR v3 header.</summary>
[GenerateSerializer]
public readonly partial record struct PvrHeader(
  [property: HeaderField(0, 4)] uint Version,
  [property: HeaderField(4, 4)] uint Flags,
  [property: HeaderField(8, 8)] ulong PixelFormat,
  [property: HeaderField(16, 4)] uint ColorSpace,
  [property: HeaderField(20, 4)] uint ChannelType,
  [property: HeaderField(24, 4)] uint Height,
  [property: HeaderField(28, 4)] uint Width,
  [property: HeaderField(32, 4)] uint Depth,
  [property: HeaderField(36, 4)] uint Surfaces,
  [property: HeaderField(40, 4)] uint Faces,
  [property: HeaderField(44, 4)] uint MipmapCount,
  [property: HeaderField(48, 4)] uint MetadataSize
) {

  public const int StructSize = 52;
  public const uint Magic = 0x03525650;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<PvrHeader>();
}
