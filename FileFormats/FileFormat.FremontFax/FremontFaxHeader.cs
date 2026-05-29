using FileFormat.Core;

namespace FileFormat.FremontFax;

[GenerateSerializer]
internal readonly partial record struct FremontFaxHeader(
  [property: FieldOffset(4)] ushort Width,
  ushort Height
) {
  public const int StructSize = 8;
}
