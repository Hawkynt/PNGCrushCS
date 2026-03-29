using FileFormat.Core;

namespace FileFormat.Jng;

/// <summary>The 16-byte JHDR chunk data in a JNG file.</summary>
[GenerateSerializer]
public readonly partial record struct JngHeader(
  [property: HeaderField(0, 4, Endianness = Endianness.Big)] int Width,
  [property: HeaderField(4, 4, Endianness = Endianness.Big)] int Height,
  [property: HeaderField(8, 1)] byte ColorType,
  [property: HeaderField(9, 1)] byte ImageSampleDepth,
  [property: HeaderField(10, 1)] byte ImageCompressionMethod,
  [property: HeaderField(11, 1)] byte ImageInterlaceMethod,
  [property: HeaderField(12, 1)] byte AlphaSampleDepth,
  [property: HeaderField(13, 1)] byte AlphaCompressionMethod,
  [property: HeaderField(14, 1)] byte AlphaFilterMethod,
  [property: HeaderField(15, 1)] byte AlphaInterlaceMethod
) {

  public const int StructSize = 16;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<JngHeader>();
}
