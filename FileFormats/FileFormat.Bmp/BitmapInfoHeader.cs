using FileFormat.Core;

namespace FileFormat.Bmp;

/// <summary>The 40-byte BITMAPINFOHEADER following the file header.</summary>
[GenerateSerializer]
public readonly partial record struct BitmapInfoHeader(
  int HeaderSize,
  int Width,
  int Height,
  short Planes,
  short BitsPerPixel,
  int Compression,
  int ImageSize,
  int XPixelsPerMeter,
  int YPixelsPerMeter,
  int ColorsUsed,
  int ImportantColors
) {

 public const int StructSize = 40;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<BitmapInfoHeader>();
}
