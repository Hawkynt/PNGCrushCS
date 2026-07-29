using FileFormat.Core;

namespace FileFormat.ImagingFax;

[GenerateSerializer]
internal readonly partial record struct ImagingFaxHeader(
  [property: FieldOffset(4)] ushort Width,
  ushort Height,
  ushort Encoding,
  ushort Flags
) {
  public const int StructSize = 12;
}
