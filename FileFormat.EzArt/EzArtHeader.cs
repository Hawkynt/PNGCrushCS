using FileFormat.Core;

namespace FileFormat.EzArt;

[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct EzArtHeader(
  [property: Repeat(16)] short[] Palette
) {
  public const int StructSize = 32;
}
