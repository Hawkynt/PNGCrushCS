using FileFormat.Core;

namespace FileFormat.UtahRle;

/// <summary>The 15-byte core header of a Utah RLE file.</summary>
/// <remarks>
/// The last byte says how many entries a colour-map channel has, as a power of two. It was missing
/// here, so the header ran a byte short and every reader took the first opcode of the picture as
/// that count — which is why nothing would open what this wrote.
/// </remarks>
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
  byte NumColorMapChannels,
  byte ColorMapLengthLog2
) {

 public const int StructSize = 15;
 public const short MagicValue = unchecked((short)0xCC52);

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<UtahRleHeader>();
}
