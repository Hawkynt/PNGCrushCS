using FileFormat.Core;

namespace FileFormat.SymbianMbm;

/// <summary>The 40-byte bitmap header for each bitmap entry in an MBM file.</summary>
[GenerateSerializer]
public readonly partial record struct SymbianMbmBitmapHeader(
  [property: HeaderField(0, 4)] int HeaderSize,
  [property: HeaderField(4, 4)] int HeaderLength,
  [property: HeaderField(8, 4)] int Width,
  [property: HeaderField(12, 4)] int Height,
  [property: HeaderField(16, 4)] int BitsPerPixel,
  [property: HeaderField(20, 4)] uint ColorMode,
  [property: HeaderField(24, 4)] uint Compression,
  [property: HeaderField(28, 4)] uint PaletteSize,
  [property: HeaderField(32, 4)] uint DataSize,
  [property: HeaderField(36, 4)] int Padding
) {

  public const int StructSize = 40;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<SymbianMbmBitmapHeader>();
}
