using FileFormat.Core;

namespace FileFormat.Bmp;

/// <summary>The 40-byte BITMAPINFOHEADER following the file header.</summary>
[GenerateSerializer]
public readonly partial record struct BitmapInfoHeader(
  [property: HeaderField(0, 4)] int HeaderSize,
  [property: HeaderField(4, 4)] int Width,
  [property: HeaderField(8, 4)] int Height,
  [property: HeaderField(12, 2)] short Planes,
  [property: HeaderField(14, 2)] short BitsPerPixel,
  [property: HeaderField(16, 4)] int Compression,
  [property: HeaderField(20, 4)] int ImageSize,
  [property: HeaderField(24, 4)] int XPixelsPerMeter,
  [property: HeaderField(28, 4)] int YPixelsPerMeter,
  [property: HeaderField(32, 4)] int ColorsUsed,
  [property: HeaderField(36, 4)] int ImportantColors
) {

  public const int StructSize = 40;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<BitmapInfoHeader>();
}
