using FileFormat.Core;

namespace FileFormat.TeliFax;

[GenerateSerializer]
internal readonly partial record struct TeliFaxHeader(
  [property: FieldOffset(2)] ushort Version,
  ushort Width,
  ushort Height
) {
  public const int StructSize = 8;
}
