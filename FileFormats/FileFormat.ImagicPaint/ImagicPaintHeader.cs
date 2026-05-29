using FileFormat.Core;

namespace FileFormat.ImagicPaint;

/// <summary>The 34-byte header at the start of every Imagic Paint file (2-byte resolution + 32-byte palette).</summary>
[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct ImagicPaintHeader(
  short Resolution,
  [property: Repeat(16)] short[] Palette
) {
  public const int StructSize = 34;
}
