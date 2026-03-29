using FileFormat.Core;

namespace FileFormat.Neochrome;

/// <summary>The 128-byte header at the start of every NEOchrome file. All fields are big-endian.</summary>
[GenerateSerializer]
[HeaderFiller("Reserved", 48, 80)]
public readonly partial record struct NeochromeHeader(
  [property: HeaderField(0, 2, Endianness = Endianness.Big)] short Flag,
  [property: HeaderField(2, 2, Endianness = Endianness.Big)] short Resolution,
  [property: HeaderField(4, 2, Endianness = Endianness.Big)] short Pal0,
  [property: HeaderField(6, 2, Endianness = Endianness.Big)] short Pal1,
  [property: HeaderField(8, 2, Endianness = Endianness.Big)] short Pal2,
  [property: HeaderField(10, 2, Endianness = Endianness.Big)] short Pal3,
  [property: HeaderField(12, 2, Endianness = Endianness.Big)] short Pal4,
  [property: HeaderField(14, 2, Endianness = Endianness.Big)] short Pal5,
  [property: HeaderField(16, 2, Endianness = Endianness.Big)] short Pal6,
  [property: HeaderField(18, 2, Endianness = Endianness.Big)] short Pal7,
  [property: HeaderField(20, 2, Endianness = Endianness.Big)] short Pal8,
  [property: HeaderField(22, 2, Endianness = Endianness.Big)] short Pal9,
  [property: HeaderField(24, 2, Endianness = Endianness.Big)] short Pal10,
  [property: HeaderField(26, 2, Endianness = Endianness.Big)] short Pal11,
  [property: HeaderField(28, 2, Endianness = Endianness.Big)] short Pal12,
  [property: HeaderField(30, 2, Endianness = Endianness.Big)] short Pal13,
  [property: HeaderField(32, 2, Endianness = Endianness.Big)] short Pal14,
  [property: HeaderField(34, 2, Endianness = Endianness.Big)] short Pal15,
  [property: HeaderField(36, 1)] byte AnimSpeed,
  [property: HeaderField(37, 1)] byte AnimDirection,
  [property: HeaderField(38, 2, Endianness = Endianness.Big)] short AnimSteps,
  [property: HeaderField(40, 2, Endianness = Endianness.Big)] short AnimXOffset,
  [property: HeaderField(42, 2, Endianness = Endianness.Big)] short AnimYOffset,
  [property: HeaderField(44, 2, Endianness = Endianness.Big)] short AnimWidth,
  [property: HeaderField(46, 2, Endianness = Endianness.Big)] short AnimHeight
) {

  public const int StructSize = 128;

  /// <summary>Extracts the 16-entry palette from individual fields.</summary>
  public short[] GetPalette() => [
    this.Pal0, this.Pal1, this.Pal2, this.Pal3,
    this.Pal4, this.Pal5, this.Pal6, this.Pal7,
    this.Pal8, this.Pal9, this.Pal10, this.Pal11,
    this.Pal12, this.Pal13, this.Pal14, this.Pal15
  ];

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<NeochromeHeader>();
}
