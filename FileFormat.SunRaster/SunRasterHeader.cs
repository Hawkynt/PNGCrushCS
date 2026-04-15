using FileFormat.Core;

namespace FileFormat.SunRaster;

/// <summary>The 32-byte header at the start of every Sun Raster file. All fields are big-endian.</summary>
[GenerateSerializer, Endian(Endianness.Big)]
public readonly partial record struct SunRasterHeader(
  int Magic,
  int Width,
  int Height,
  int Depth,
  int Length,
  int Type,
  int MapType,
  int MapLength
) {

 public const int StructSize = 32;
 public const int MagicValue = unchecked((int)0x59A66A95);

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<SunRasterHeader>();
}
