using FileFormat.Core;

namespace FileFormat.OazFax;

[GenerateSerializer]
internal readonly partial record struct OazFaxHeader(
  [property: FieldOffset(4)] ushort Version,
  ushort Width,
  ushort Height,
  ushort Encoding
) {
  public const int StructSize = 12;
}
