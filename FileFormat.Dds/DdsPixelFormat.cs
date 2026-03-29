using FileFormat.Core;

namespace FileFormat.Dds;

/// <summary>The 32-byte DDS_PIXELFORMAT structure embedded in the DDS header.</summary>
[GenerateSerializer]
public readonly partial record struct DdsPixelFormat(
  [property: HeaderField(0, 4)] int Size,
  [property: HeaderField(4, 4)] int Flags,
  [property: HeaderField(8, 4)] int FourCC,
  [property: HeaderField(12, 4)] int RGBBitCount,
  [property: HeaderField(16, 4)] int RBitMask,
  [property: HeaderField(20, 4)] int GBitMask,
  [property: HeaderField(24, 4)] int BBitMask,
  [property: HeaderField(28, 4)] int ABitMask
) {

  public const int StructSize = 32;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<DdsPixelFormat>();
}
