using FileFormat.Core;

namespace FileFormat.IffRgbn;

/// <summary>The 20-byte BMHD (Bitmap Header) chunk in an IFF RGBN file.</summary>
[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct RgbnBmhdChunk(
  ushort Width,
  ushort Height,
  short XOrigin,
  short YOrigin,
  byte NumPlanes,
  byte Masking,
  byte Compression,
  byte Padding,
  ushort TransparentColor,
  byte XAspect,
  byte YAspect,
  short PageWidth,
  short PageHeight
) {

 public const int StructSize = 20;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<RgbnBmhdChunk>();
}
