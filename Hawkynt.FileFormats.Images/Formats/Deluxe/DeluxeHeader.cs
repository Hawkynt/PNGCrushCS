using FileFormat.Core;

namespace FileFormat.Deluxe;

/// <summary>The 34-byte header at the start of every Deluxe Paint ST file (2-byte resolution + 32-byte palette).</summary>
[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct DeluxeHeader(
  short Resolution,
  [property: Repeat(16)] short[] Palette
) {
  public const int StructSize = 34;
}
