using FileFormat.Core;

namespace FileFormat.AtariPaintworks;

/// <summary>The 32-byte palette header for Atari ST Paintworks/GFA/DeskPic screen files (16 x big-endian short).</summary>
[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct AtariPaintworksHeader(
  [property: Repeat(16)] short[] Palette
) {
  public const int StructSize = 32;
}
