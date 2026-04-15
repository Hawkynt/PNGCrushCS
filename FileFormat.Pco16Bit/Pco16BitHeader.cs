using FileFormat.Core;

namespace FileFormat.Pco16Bit;

[GenerateSerializer]
internal readonly partial record struct Pco16BitHeader(
  int Width,
  int Height
) {
  public const int StructSize = 8;
}
