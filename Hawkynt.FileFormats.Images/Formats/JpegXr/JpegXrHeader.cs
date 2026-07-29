using FileFormat.Core;

namespace FileFormat.JpegXr;

[GenerateSerializer]
internal readonly partial record struct JpegXrHeader(
  [property: FieldOffset(2)] ushort Magic,
  uint IfdOffset
) {
  public const int StructSize = 8;
}
