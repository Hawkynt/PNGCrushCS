using FileFormat.Core;

namespace FileFormat.CompW;

[GenerateSerializer]
internal readonly partial record struct CompWHeader(
  [property: FieldOffset(2)] ushort Width,
  ushort Height,
  ushort Bpp
) {
  public const int StructSize = 8;
}
