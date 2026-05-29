using FileFormat.Core;

namespace FileFormat.SciFax;

[GenerateSerializer]
internal readonly partial record struct SciFaxHeader(
  [property: FieldOffset(2)] ushort Version,
  ushort Width,
  ushort Height
) {
  public const int StructSize = 8;
}
