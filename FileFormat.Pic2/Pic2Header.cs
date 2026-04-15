using FileFormat.Core;

namespace FileFormat.Pic2;

[GenerateSerializer]
internal readonly partial record struct Pic2Header(
  [property: FieldOffset(4)] ushort Width,
  ushort Height,
  ushort Bpp,
  ushort Mode
) {
  public const int StructSize = 12;
}
