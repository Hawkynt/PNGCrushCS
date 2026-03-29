using FileFormat.Core;

namespace FileFormat.Deluxe;

/// <summary>The 34-byte header at the start of every Deluxe Paint ST file (2-byte resolution + 32-byte palette).</summary>
[GenerateSerializer]
internal readonly partial record struct DeluxeHeader(
  [property: HeaderField(0, 2, Endianness = Endianness.Big)] short Resolution,
  [property: HeaderField(2, 2, Endianness = Endianness.Big)] short Palette0,
  [property: HeaderField(4, 2, Endianness = Endianness.Big)] short Palette1,
  [property: HeaderField(6, 2, Endianness = Endianness.Big)] short Palette2,
  [property: HeaderField(8, 2, Endianness = Endianness.Big)] short Palette3,
  [property: HeaderField(10, 2, Endianness = Endianness.Big)] short Palette4,
  [property: HeaderField(12, 2, Endianness = Endianness.Big)] short Palette5,
  [property: HeaderField(14, 2, Endianness = Endianness.Big)] short Palette6,
  [property: HeaderField(16, 2, Endianness = Endianness.Big)] short Palette7,
  [property: HeaderField(18, 2, Endianness = Endianness.Big)] short Palette8,
  [property: HeaderField(20, 2, Endianness = Endianness.Big)] short Palette9,
  [property: HeaderField(22, 2, Endianness = Endianness.Big)] short Palette10,
  [property: HeaderField(24, 2, Endianness = Endianness.Big)] short Palette11,
  [property: HeaderField(26, 2, Endianness = Endianness.Big)] short Palette12,
  [property: HeaderField(28, 2, Endianness = Endianness.Big)] short Palette13,
  [property: HeaderField(30, 2, Endianness = Endianness.Big)] short Palette14,
  [property: HeaderField(32, 2, Endianness = Endianness.Big)] short Palette15
) {

  public const int StructSize = 34;

  public short[] GetPaletteArray() => [
    this.Palette0, this.Palette1, this.Palette2, this.Palette3,
    this.Palette4, this.Palette5, this.Palette6, this.Palette7,
    this.Palette8, this.Palette9, this.Palette10, this.Palette11,
    this.Palette12, this.Palette13, this.Palette14, this.Palette15
  ];

  public static DeluxeHeader FromPalette(short resolution, short[] palette) => new(
    resolution,
    palette[0], palette[1], palette[2], palette[3],
    palette[4], palette[5], palette[6], palette[7],
    palette[8], palette[9], palette[10], palette[11],
    palette[12], palette[13], palette[14], palette[15]
  );

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<DeluxeHeader>();
}
