using FileFormat.Core;

namespace FileFormat.Fsh;

[GenerateSerializer]
internal readonly partial record struct FshHeader(
  [property: FieldOffset(4)] int FileSize,
  int EntryCount
) {
  public const int StructSize = 12;
}
