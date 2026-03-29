using FileFormat.Core;

namespace FileFormat.IffRgbn;

/// <summary>The 20-byte BMHD (Bitmap Header) chunk in an IFF RGBN file.</summary>
[GenerateSerializer]
internal readonly partial record struct RgbnBmhdChunk(
  [property: HeaderField(0, 2, Endianness = Endianness.Big)] ushort Width,
  [property: HeaderField(2, 2, Endianness = Endianness.Big)] ushort Height,
  [property: HeaderField(4, 2, Endianness = Endianness.Big)] short XOrigin,
  [property: HeaderField(6, 2, Endianness = Endianness.Big)] short YOrigin,
  [property: HeaderField(8, 1)] byte NumPlanes,
  [property: HeaderField(9, 1)] byte Masking,
  [property: HeaderField(10, 1)] byte Compression,
  [property: HeaderField(11, 1)] byte Padding,
  [property: HeaderField(12, 2, Endianness = Endianness.Big)] ushort TransparentColor,
  [property: HeaderField(14, 1)] byte XAspect,
  [property: HeaderField(15, 1)] byte YAspect,
  [property: HeaderField(16, 2, Endianness = Endianness.Big)] short PageWidth,
  [property: HeaderField(18, 2, Endianness = Endianness.Big)] short PageHeight
) {

  public const int StructSize = 20;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<RgbnBmhdChunk>();
}
