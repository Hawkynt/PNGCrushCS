using FileFormat.Core;

namespace FileFormat.Bmp;

/// <summary>The 14-byte BITMAPFILEHEADER at the start of every BMP file.</summary>
[GenerateSerializer]
public readonly partial record struct BitmapFileHeader(
  [property: HeaderField(0, 1)] byte Sig1,
  [property: HeaderField(1, 1)] byte Sig2,
  [property: HeaderField(2, 4)] int FileSize,
  [property: HeaderField(6, 2)] short Reserved1,
  [property: HeaderField(8, 2)] short Reserved2,
  [property: HeaderField(10, 4)] int PixelDataOffset
) {

  public const int StructSize = 14;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<BitmapFileHeader>();
}
