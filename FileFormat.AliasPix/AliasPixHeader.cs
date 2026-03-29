using FileFormat.Core;

namespace FileFormat.AliasPix;

/// <summary>The 10-byte header at the start of every Alias/Wavefront PIX file: Width, Height, XOffset, YOffset, BitsPerPixel (all ushort BE).</summary>
[GenerateSerializer]
public readonly partial record struct AliasPixHeader(
  [property: HeaderField(0, 2, Endianness = Endianness.Big)] ushort Width,
  [property: HeaderField(2, 2, Endianness = Endianness.Big)] ushort Height,
  [property: HeaderField(4, 2, Endianness = Endianness.Big)] ushort XOffset,
  [property: HeaderField(6, 2, Endianness = Endianness.Big)] ushort YOffset,
  [property: HeaderField(8, 2, Endianness = Endianness.Big)] ushort BitsPerPixel
) {

  public const int StructSize = 10;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<AliasPixHeader>();
}
