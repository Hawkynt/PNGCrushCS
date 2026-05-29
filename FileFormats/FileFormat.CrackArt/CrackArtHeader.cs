using FileFormat.Core;

namespace FileFormat.CrackArt;

/// <summary>The 33-byte header at the start of every CrackArt file: 1-byte resolution + 16 big-endian palette shorts.</summary>
[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct CrackArtHeader(
  short Resolution,
  [property: Repeat(16)] short[] Palette
) {
  public const int StructSize = 34;
}
