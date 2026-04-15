using FileFormat.Core;

namespace FileFormat.AdexImage;

[GenerateSerializer]
internal readonly partial record struct AdexImageHeader(
  [property: FieldOffset(4)] ushort Width,
  ushort Height,
  ushort Bpp,
  ushort Compression
) {
  public const int StructSize = 12;
}
