using FileFormat.Core;

namespace FileFormat.Fl32;

[GenerateSerializer]
internal readonly partial record struct Fl32Header(
  [property: FieldOffset(4)] int Height,
  int Width,
  int Channels
) {
  public const int StructSize = 16;
}
