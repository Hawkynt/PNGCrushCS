using FileFormat.Core;

namespace FileFormat.Im5Visilog;

[GenerateSerializer]
internal readonly partial record struct Im5VisilogHeader(
  int Width,
  int Height,
  int Depth
) {
  public const int StructSize = 12;
}
