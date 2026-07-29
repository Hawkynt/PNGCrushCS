using FileFormat.Core;

namespace FileFormat.SymbianMbm;

/// <summary>The 40-byte bitmap header for each bitmap entry in an MBM file.</summary>
[GenerateSerializer]
public readonly partial record struct SymbianMbmBitmapHeader(
  int HeaderSize,
  int HeaderLength,
  int Width,
  int Height,
  int BitsPerPixel,
  uint ColorMode,
  uint Compression,
  uint PaletteSize,
  uint DataSize,
  int Padding
) {

 public const int StructSize = 40;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<SymbianMbmBitmapHeader>();
}
