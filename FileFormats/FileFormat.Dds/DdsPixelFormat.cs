using FileFormat.Core;

namespace FileFormat.Dds;

/// <summary>The 32-byte DDS_PIXELFORMAT structure embedded in the DDS header.</summary>
[GenerateSerializer]
public readonly partial record struct DdsPixelFormat(
  int Size,
  int Flags,
  int FourCC,
  int RGBBitCount,
  int RBitMask,
  int GBitMask,
  int BBitMask,
  int ABitMask
) {

 public const int StructSize = 32;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<DdsPixelFormat>();
}
