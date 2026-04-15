using FileFormat.Core;

namespace FileFormat.AimGrayScale;

[GenerateSerializer]
internal readonly partial record struct AimGrayScaleHeader(
  [property: FieldOffset(4)] ushort Width,
  ushort Height
) {
  public const int StructSize = 8;
}
