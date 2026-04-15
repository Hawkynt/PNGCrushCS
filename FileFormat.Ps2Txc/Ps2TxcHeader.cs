using FileFormat.Core;

namespace FileFormat.Ps2Txc;

[GenerateSerializer]
internal readonly partial record struct Ps2TxcHeader(
  ushort Width,
  ushort Height,
  ushort Bpp,
  ushort Flags
) {
  public const int StructSize = 8;
}
