using FileFormat.Core;

namespace FileFormat.SeqImage;

[GenerateSerializer]
internal readonly partial record struct SeqImageHeader(
  [property: FieldOffset(4)] ushort Version,
  ushort Width,
  ushort Height,
  ushort FrameCount,
  ushort Bpp
) {
  public const int StructSize = 14;
}
