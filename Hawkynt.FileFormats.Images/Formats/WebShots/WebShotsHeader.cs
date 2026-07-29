using FileFormat.Core;

namespace FileFormat.WebShots;

[GenerateSerializer]
internal readonly partial record struct WebShotsHeader(
  [property: FieldOffset(4)] ushort Version,
  ushort Width,
  ushort Height,
  ushort Bpp
) {
  public const int StructSize = 12;
}
