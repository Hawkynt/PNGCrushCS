using FileFormat.Core;

namespace FileFormat.VentaFax;

[GenerateSerializer]
internal readonly partial record struct VentaFaxHeader(
  [property: FieldOffset(4)] ushort Version,
  ushort Width,
  ushort Height,
  ushort Encoding
) {
  public const int StructSize = 12;
}
