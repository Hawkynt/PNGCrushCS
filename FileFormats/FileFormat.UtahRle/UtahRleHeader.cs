using FileFormat.Core;

namespace FileFormat.UtahRle;

/// <summary>The 14-byte core header of a Utah RLE file.</summary>
[GenerateSerializer]
public readonly partial record struct UtahRleHeader(
  short Magic,
  short XPos,
  short YPos,
  short XSize,
  short YSize,
  byte Flags,
  byte NumChannels,
  byte NumBitsPerPixel,
  byte NumColorMapChannels
) {

 public const int StructSize = 14;
 public const short MagicValue = unchecked((short)0xCC52);

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<UtahRleHeader>();
}
