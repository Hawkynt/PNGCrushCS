using FileFormat.Core;

namespace FileFormat.Calamus;

[GenerateSerializer]
internal readonly partial record struct CalamusHeader(
  [property: FieldOffset(4)] ushort Version,
  ushort Width,
  ushort Height,
  ushort Bpp
) {
  public const int StructSize = 12;
}
