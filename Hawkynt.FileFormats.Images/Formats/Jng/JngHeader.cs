using FileFormat.Core;

namespace FileFormat.Jng;

/// <summary>The 16-byte JHDR chunk data in a JNG file.</summary>
[GenerateSerializer, Endian(Endianness.Big)]
public readonly partial record struct JngHeader(
  int Width,
  int Height,
  byte ColorType,
  byte ImageSampleDepth,
  byte ImageCompressionMethod,
  byte ImageInterlaceMethod,
  byte AlphaSampleDepth,
  byte AlphaCompressionMethod,
  byte AlphaFilterMethod,
  byte AlphaInterlaceMethod
) {

 public const int StructSize = 16;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<JngHeader>();
}
