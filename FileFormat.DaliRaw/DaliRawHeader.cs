using FileFormat.Core;

namespace FileFormat.DaliRaw;

[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct DaliRawHeader(
  [property: Repeat(16)] short[] Palette
) {
  public const int StructSize = 32;
}
