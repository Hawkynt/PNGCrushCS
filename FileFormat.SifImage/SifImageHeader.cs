using FileFormat.Core;

namespace FileFormat.SifImage;

[GenerateSerializer]
internal readonly partial record struct SifImageHeader(
  [property: FieldOffset(4)] ushort Width,
  ushort Height,
  ushort Bpp
) {
  public const int StructSize = 10;
}
