using FileFormat.Core;

namespace FileFormat.Avs;

[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct AvsHeader(
  uint Width,
  uint Height
) {
  public const int StructSize = 8;
}
