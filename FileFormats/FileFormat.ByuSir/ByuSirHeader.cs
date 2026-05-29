using FileFormat.Core;

namespace FileFormat.ByuSir;

[GenerateSerializer]
internal readonly partial record struct ByuSirHeader(
  [property: FieldOffset(4)] ushort Width,
  ushort Height,
  ushort DataType,
  ushort Reserved
) {
  public const int StructSize = 12;
}
