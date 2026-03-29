using FileFormat.Core;

namespace FileFormat.SyntheticArts;

/// <summary>The 32-byte palette header at the start of every Synthetic Arts file. All fields are big-endian.</summary>
[GenerateSerializer]
internal readonly partial record struct SyntheticArtsHeader(
  [property: HeaderField(0, 2, Endianness = Endianness.Big)] short Pal0,
  [property: HeaderField(2, 2, Endianness = Endianness.Big)] short Pal1,
  [property: HeaderField(4, 2, Endianness = Endianness.Big)] short Pal2,
  [property: HeaderField(6, 2, Endianness = Endianness.Big)] short Pal3,
  [property: HeaderField(8, 2, Endianness = Endianness.Big)] short Pal4,
  [property: HeaderField(10, 2, Endianness = Endianness.Big)] short Pal5,
  [property: HeaderField(12, 2, Endianness = Endianness.Big)] short Pal6,
  [property: HeaderField(14, 2, Endianness = Endianness.Big)] short Pal7,
  [property: HeaderField(16, 2, Endianness = Endianness.Big)] short Pal8,
  [property: HeaderField(18, 2, Endianness = Endianness.Big)] short Pal9,
  [property: HeaderField(20, 2, Endianness = Endianness.Big)] short Pal10,
  [property: HeaderField(22, 2, Endianness = Endianness.Big)] short Pal11,
  [property: HeaderField(24, 2, Endianness = Endianness.Big)] short Pal12,
  [property: HeaderField(26, 2, Endianness = Endianness.Big)] short Pal13,
  [property: HeaderField(28, 2, Endianness = Endianness.Big)] short Pal14,
  [property: HeaderField(30, 2, Endianness = Endianness.Big)] short Pal15
) {

  public const int StructSize = 32;

  /// <summary>Extracts the 16-entry palette from individual fields (only first 4 are meaningful for medium-res).</summary>
  public short[] GetPaletteArray() => [
    this.Pal0, this.Pal1, this.Pal2, this.Pal3,
    this.Pal4, this.Pal5, this.Pal6, this.Pal7,
    this.Pal8, this.Pal9, this.Pal10, this.Pal11,
    this.Pal12, this.Pal13, this.Pal14, this.Pal15
  ];

  public static SyntheticArtsHeader FromPalette(short[] palette) => new(
    palette[0], palette[1], palette[2], palette[3],
    palette[4], palette[5], palette[6], palette[7],
    palette[8], palette[9], palette[10], palette[11],
    palette[12], palette[13], palette[14], palette[15]
  );

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<SyntheticArtsHeader>();
}
