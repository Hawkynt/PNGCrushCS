using FileFormat.Core;

namespace FileFormat.ScreenBlaster;

/// <summary>The 34-byte header at the start of every Screen Blaster file (2-byte resolution + 32-byte palette).</summary>
[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct ScreenBlasterHeader(
  short Resolution,
  [property: Repeat(16)] short[] Palette
) {
  public const int StructSize = 34;
}
