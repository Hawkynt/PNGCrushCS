using FileFormat.Core;

namespace FileFormat.FontasyGrafik;

[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct FontasyGrafikHeader(
  [property: Repeat(16)] short[] Palette
) {
  public const int StructSize = 32;
}
