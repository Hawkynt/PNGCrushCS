using FileFormat.Core;

namespace FileFormat.MobileFax;

[GenerateSerializer]
internal readonly partial record struct MobileFaxHeader(
  [property: FieldOffset(2)] ushort Version,
  ushort Width,
  ushort Height
) {
  public const int StructSize = 8;
}
