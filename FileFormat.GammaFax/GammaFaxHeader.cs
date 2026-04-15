using FileFormat.Core;

namespace FileFormat.GammaFax;

[GenerateSerializer]
internal readonly partial record struct GammaFaxHeader(
  [property: FieldOffset(2)] ushort Version,
  ushort Width,
  ushort Height,
  ushort Compression
) {
  public const int StructSize = 10;
}
