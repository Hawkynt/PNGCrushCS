using FileFormat.Core;

namespace FileFormat.Rlc2;

[GenerateSerializer]
internal readonly partial record struct Rlc2Header(
  [property: FieldOffset(4)] ushort Width,
  ushort Height,
  ushort Bpp
) {
  public const int StructSize = 10;
}
