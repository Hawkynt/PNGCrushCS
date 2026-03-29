using FileFormat.Core;

namespace FileFormat.Psb;

/// <summary>The 26-byte file header at the start of every PSD/PSB file (all big-endian).</summary>
[GenerateSerializer]
public readonly partial record struct PsbHeader(
  [property: HeaderField(0, 1)] byte Sig0,
  [property: HeaderField(1, 1)] byte Sig1,
  [property: HeaderField(2, 1)] byte Sig2,
  [property: HeaderField(3, 1)] byte Sig3,
  [property: HeaderField(4, 2, Endianness = Endianness.Big)] short Version,
  [property: HeaderField(6, 1)] byte Reserved0,
  [property: HeaderField(7, 1)] byte Reserved1,
  [property: HeaderField(8, 1)] byte Reserved2,
  [property: HeaderField(9, 1)] byte Reserved3,
  [property: HeaderField(10, 1)] byte Reserved4,
  [property: HeaderField(11, 1)] byte Reserved5,
  [property: HeaderField(12, 2, Endianness = Endianness.Big)] short Channels,
  [property: HeaderField(14, 4, Endianness = Endianness.Big)] int Height,
  [property: HeaderField(18, 4, Endianness = Endianness.Big)] int Width,
  [property: HeaderField(22, 2, Endianness = Endianness.Big)] short Depth,
  [property: HeaderField(24, 2, Endianness = Endianness.Big)] short ColorMode
) {

  public const int StructSize = 26;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<PsbHeader>();
}
