using FileFormat.Core;

namespace FileFormat.SyntheticArts;

/// <summary>The 32-byte palette header at the start of every Synthetic Arts file. All fields are big-endian.</summary>
[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct SyntheticArtsHeader(
  [property: Repeat(16)] short[] Palette
) {
  public const int StructSize = 32;
}
