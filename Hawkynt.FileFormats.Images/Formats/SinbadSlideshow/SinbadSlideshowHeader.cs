using FileFormat.Core;

namespace FileFormat.SinbadSlideshow;

[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct SinbadSlideshowHeader(
  [property: Repeat(16)] short[] Palette
) {
  public const int StructSize = 32;
}
