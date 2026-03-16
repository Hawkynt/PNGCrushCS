using FileFormat.Core;

namespace FileFormat.Png;

/// <summary>The 13-byte IHDR chunk data in a PNG file.</summary>
[GenerateSerializer]
public readonly partial record struct PngIhdr(
  [property: HeaderField(0, 4, Endianness = Endianness.Big)] int Width,
  [property: HeaderField(4, 4, Endianness = Endianness.Big)] int Height,
  [property: HeaderField(8, 1)] byte BitDepth,
  [property: HeaderField(9, 1)] byte ColorType,
  [property: HeaderField(10, 1)] byte CompressionMethod,
  [property: HeaderField(11, 1)] byte FilterMethod,
  [property: HeaderField(12, 1)] byte InterlaceMethod
) {

  public const int StructSize = 13;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<PngIhdr>();
}
