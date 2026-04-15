using FileFormat.Core;

namespace FileFormat.LucasFilm;

[GenerateSerializer]
internal readonly partial record struct LucasFilmHeader(
  [property: FieldOffset(4)] ushort Width,
  ushort Height,
  ushort Bpp,
  ushort Channels,
  uint Reserved
) {
  public const int StructSize = 16;
}
