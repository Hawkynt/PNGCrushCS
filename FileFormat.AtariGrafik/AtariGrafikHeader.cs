using FileFormat.Core;

namespace FileFormat.AtariGrafik;

/// <summary>The 2-byte header at offset 0: Resolution (short BE).</summary>
[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct AtariGrafikHeader(
  short Resolution
) {
  public const int StructSize = 2;
}
