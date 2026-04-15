using FileFormat.Core;

namespace FileFormat.Degas;

/// <summary>The 34-byte header at the start of every DEGAS file.</summary>
[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct DegasHeader(
  short Resolution,
  [property: Repeat(16)] short[] Palette
) {
  public const int StructSize = 34;
}
