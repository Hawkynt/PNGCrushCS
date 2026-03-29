using FileFormat.Core;

namespace FileFormat.SunRaster;

/// <summary>The 32-byte header at the start of every Sun Raster file. All fields are big-endian.</summary>
[GenerateSerializer]
public readonly partial record struct SunRasterHeader(
  [property: HeaderField(0, 4, Endianness = Endianness.Big)] int Magic,
  [property: HeaderField(4, 4, Endianness = Endianness.Big)] int Width,
  [property: HeaderField(8, 4, Endianness = Endianness.Big)] int Height,
  [property: HeaderField(12, 4, Endianness = Endianness.Big)] int Depth,
  [property: HeaderField(16, 4, Endianness = Endianness.Big)] int Length,
  [property: HeaderField(20, 4, Endianness = Endianness.Big)] int Type,
  [property: HeaderField(24, 4, Endianness = Endianness.Big)] int MapType,
  [property: HeaderField(28, 4, Endianness = Endianness.Big)] int MapLength
) {

  public const int StructSize = 32;
  public const int MagicValue = unchecked((int)0x59A66A95);

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<SunRasterHeader>();
}
