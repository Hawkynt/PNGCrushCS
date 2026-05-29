using FileFormat.Core;

namespace FileFormat.SonyMavica;

[GenerateSerializer]
internal readonly partial record struct SonyMavicaHeader(
  [property: FieldOffset(2)] ushort Width,
  ushort Height,
  ushort Format
) {
  public const int StructSize = 8;
}
