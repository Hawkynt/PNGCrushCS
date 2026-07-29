using FileFormat.Core;

namespace FileFormat.Aai;

[GenerateSerializer]
internal readonly partial record struct AaiHeader(
  uint Width,
  uint Height
) {
  public const int StructSize = 8;
}
