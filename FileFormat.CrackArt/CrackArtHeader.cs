using FileFormat.Core;

namespace FileFormat.CrackArt;

/// <summary>The 33-byte header at the start of every CrackArt file: 1-byte resolution + 16 big-endian palette shorts.</summary>
[GenerateSerializer]
internal readonly partial record struct CrackArtHeader(
  [property: HeaderField(0, 1)] byte Resolution,
  [property: HeaderField(1, 2, Endianness = Endianness.Big)] short Palette0,
  [property: HeaderField(3, 2, Endianness = Endianness.Big)] short Palette1,
  [property: HeaderField(5, 2, Endianness = Endianness.Big)] short Palette2,
  [property: HeaderField(7, 2, Endianness = Endianness.Big)] short Palette3,
  [property: HeaderField(9, 2, Endianness = Endianness.Big)] short Palette4,
  [property: HeaderField(11, 2, Endianness = Endianness.Big)] short Palette5,
  [property: HeaderField(13, 2, Endianness = Endianness.Big)] short Palette6,
  [property: HeaderField(15, 2, Endianness = Endianness.Big)] short Palette7,
  [property: HeaderField(17, 2, Endianness = Endianness.Big)] short Palette8,
  [property: HeaderField(19, 2, Endianness = Endianness.Big)] short Palette9,
  [property: HeaderField(21, 2, Endianness = Endianness.Big)] short Palette10,
  [property: HeaderField(23, 2, Endianness = Endianness.Big)] short Palette11,
  [property: HeaderField(25, 2, Endianness = Endianness.Big)] short Palette12,
  [property: HeaderField(27, 2, Endianness = Endianness.Big)] short Palette13,
  [property: HeaderField(29, 2, Endianness = Endianness.Big)] short Palette14,
  [property: HeaderField(31, 2, Endianness = Endianness.Big)] short Palette15
) {

  public const int StructSize = 33;

  public short[] GetPaletteArray() => [
    this.Palette0, this.Palette1, this.Palette2, this.Palette3,
    this.Palette4, this.Palette5, this.Palette6, this.Palette7,
    this.Palette8, this.Palette9, this.Palette10, this.Palette11,
    this.Palette12, this.Palette13, this.Palette14, this.Palette15
  ];

  public static CrackArtHeader FromPalette(byte resolution, short[] palette) => new(
    resolution,
    palette[0], palette[1], palette[2], palette[3],
    palette[4], palette[5], palette[6], palette[7],
    palette[8], palette[9], palette[10], palette[11],
    palette[12], palette[13], palette[14], palette[15]
  );

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<CrackArtHeader>();
}
