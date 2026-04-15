using FileFormat.Core;

namespace FileFormat.Gbr;

[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct GbrHeader(
  [property: FieldOffset(4)] uint Version,
  uint Width,
  uint Height,
  uint BytesPerPixel
) {
  public const int StructSize = 20;
}
