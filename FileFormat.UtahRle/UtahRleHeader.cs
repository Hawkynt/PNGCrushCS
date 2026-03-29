using FileFormat.Core;

namespace FileFormat.UtahRle;

/// <summary>The 14-byte core header of a Utah RLE file.</summary>
[GenerateSerializer]
public readonly partial record struct UtahRleHeader(
  [property: HeaderField(0, 2)] short Magic,
  [property: HeaderField(2, 2)] short XPos,
  [property: HeaderField(4, 2)] short YPos,
  [property: HeaderField(6, 2)] short XSize,
  [property: HeaderField(8, 2)] short YSize,
  [property: HeaderField(10, 1)] byte Flags,
  [property: HeaderField(11, 1)] byte NumChannels,
  [property: HeaderField(12, 1)] byte NumBitsPerPixel,
  [property: HeaderField(13, 1)] byte NumColorMapChannels
) {

  public const int StructSize = 14;
  public const short MagicValue = unchecked((short)0xCC52);

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<UtahRleHeader>();
}
