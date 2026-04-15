using FileFormat.Core;

namespace FileFormat.HighresMedium;

/// <summary>The 32-byte palette header for one frame of a Highres Medium file. All fields are big-endian.</summary>
[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct HighresMediumHeader(
  [property: Repeat(16)] short[] Palette
) {
  public const int StructSize = 32;
  public const int FrameSize = StructSize + 32000;
}
