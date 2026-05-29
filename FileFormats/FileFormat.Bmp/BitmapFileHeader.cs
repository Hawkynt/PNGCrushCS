using FileFormat.Core;

namespace FileFormat.Bmp;

/// <summary>The 14-byte BITMAPFILEHEADER at the start of every BMP file.</summary>
[GenerateSerializer]
public readonly partial record struct BitmapFileHeader(
  byte Sig1,
  byte Sig2,
  int FileSize,
  short Reserved1,
  short Reserved2,
  int PixelDataOffset
) {

 public const int StructSize = 14;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<BitmapFileHeader>();
}
