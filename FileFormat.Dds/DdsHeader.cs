using FileFormat.Core;

namespace FileFormat.Dds;

/// <summary>The 124-byte DDS_HEADER structure following the 4-byte magic number.</summary>
[GenerateSerializer]
[Filler(28, 44)]
public readonly partial record struct DdsHeader(
 int Size,
 int Flags,
 int Height,
 int Width,
 int PitchOrLinearSize,
 int Depth,
 int MipMapCount,
 [property: FieldOffset(72)] DdsPixelFormat PixelFormat,
 int Caps,
 int Caps2,
 int Caps3,
 int Caps4,
 int Reserved2
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
