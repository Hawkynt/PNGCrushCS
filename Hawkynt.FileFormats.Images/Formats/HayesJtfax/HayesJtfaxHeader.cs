using FileFormat.Core;

namespace FileFormat.HayesJtfax;

[GenerateSerializer]
internal readonly partial record struct HayesJtfaxHeader(
  [property: FieldOffset(2)] ushort Version,
  ushort Width,
  ushort Height
) {
  public const int StructSize = 8;
}
