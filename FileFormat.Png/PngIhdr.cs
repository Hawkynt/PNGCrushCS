using FileFormat.Core;

namespace FileFormat.Png;

/// <summary>The 13-byte IHDR chunk data in a PNG file.</summary>
[GenerateSerializer, Endian(Endianness.Big)]
public readonly partial record struct PngIhdr(
  int Width,
  int Height,
  byte BitDepth,
  byte ColorType,
  byte CompressionMethod,
  byte FilterMethod,
  byte InterlaceMethod
) {

 public const int StructSize = 13;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<PngIhdr>();
}
