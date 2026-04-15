using FileFormat.Core;

namespace FileFormat.AutodeskCel;

[GenerateSerializer]
internal readonly partial record struct AutodeskCelHeader(
  [property: FieldOffset(2)] ushort Width,
  ushort Height,
  ushort XOffset,
  ushort YOffset,
  ushort BitsPerPixel
) {
  public const int StructSize = 12;
}
