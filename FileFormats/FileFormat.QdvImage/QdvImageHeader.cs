using FileFormat.Core;

namespace FileFormat.QdvImage;

[GenerateSerializer]
internal readonly partial record struct QdvImageHeader(
  [property: FieldOffset(4)] ushort Width,
  ushort Height,
  ushort Bpp,
  ushort Flags
) {
  public const int StructSize = 12;
}
