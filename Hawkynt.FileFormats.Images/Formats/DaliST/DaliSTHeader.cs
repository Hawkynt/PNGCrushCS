using FileFormat.Core;

namespace FileFormat.DaliST;

[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct DaliSTHeader(
  [property: Repeat(16)] short[] Palette
) {
  public const int StructSize = 32;
}
