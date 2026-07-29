using FileFormat.Core;

namespace FileFormat.Pvr;

/// <summary>The 52-byte PVR v3 header.</summary>
[GenerateSerializer]
public readonly partial record struct PvrHeader(
  uint Version,
  uint Flags,
  ulong PixelFormat,
  uint ColorSpace,
  uint ChannelType,
  uint Height,
  uint Width,
  uint Depth,
  uint Surfaces,
  uint Faces,
  uint MipmapCount,
  uint MetadataSize
) {

 public const int StructSize = 52;
 public const uint Magic = 0x03525650;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<PvrHeader>();
}
