using FileFormat.Core;

namespace FileFormat.FullscreenKit;

/// <summary>The 32-byte palette header at the start of every Fullscreen Kit file. All fields are big-endian.</summary>
[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct FullscreenKitHeader(
  [property: Repeat(16)] short[] Palette
) {
  public const int StructSize = 32;
}
