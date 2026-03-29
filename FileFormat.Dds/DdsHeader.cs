using FileFormat.Core;

namespace FileFormat.Dds;

/// <summary>The 124-byte DDS_HEADER structure following the 4-byte magic number.</summary>
[GenerateSerializer]
[HeaderFiller("Reserved1", 28, 44)]
public readonly partial record struct DdsHeader(
  [property: HeaderField(0, 4)] int Size,
  [property: HeaderField(4, 4)] int Flags,
  [property: HeaderField(8, 4)] int Height,
  [property: HeaderField(12, 4)] int Width,
  [property: HeaderField(16, 4)] int PitchOrLinearSize,
  [property: HeaderField(20, 4)] int Depth,
  [property: HeaderField(24, 4)] int MipMapCount,
  [property: HeaderField(72, 32)] DdsPixelFormat PixelFormat,
  [property: HeaderField(104, 4)] int Caps,
  [property: HeaderField(108, 4)] int Caps2,
  [property: HeaderField(112, 4)] int Caps3,
  [property: HeaderField(116, 4)] int Caps4,
  [property: HeaderField(120, 4)] int Reserved2
) {

  public const int StructSize = 124;

  // Required flags
  internal const int DDSD_CAPS = 0x1;
  internal const int DDSD_HEIGHT = 0x2;
  internal const int DDSD_WIDTH = 0x4;
  internal const int DDSD_PIXELFORMAT = 0x1000;
  internal const int DDSD_MIPMAPCOUNT = 0x20000;
  internal const int DDSD_LINEARSIZE = 0x80000;
  internal const int DDSD_DEPTH = 0x800000;

  // Pixel format flags
  internal const int DDPF_ALPHAPIXELS = 0x1;
  internal const int DDPF_FOURCC = 0x4;
  internal const int DDPF_RGB = 0x40;

  // Caps flags
  internal const int DDSCAPS_TEXTURE = 0x1000;
  internal const int DDSCAPS_MIPMAP = 0x400000;
  internal const int DDSCAPS_COMPLEX = 0x8;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<DdsHeader>();
}
