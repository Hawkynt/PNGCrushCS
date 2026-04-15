using FileFormat.Core;

namespace FileFormat.Pat;

[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct PatHeader(
  uint HeaderSize,
  uint Version,
  uint Width,
  uint Height,
  uint BytesPerPixel
) {
  public const int StructSize = 20;
}
