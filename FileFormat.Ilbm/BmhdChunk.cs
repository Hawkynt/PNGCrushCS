using FileFormat.Core;

namespace FileFormat.Ilbm;

/// <summary>The 20-byte BMHD (Bitmap Header) chunk in an ILBM file.</summary>
[GenerateSerializer, Endian(Endianness.Big)]
public readonly partial record struct BmhdChunk(
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
 => HeaderFieldMapper.GetFieldMap<BmhdChunk>();
}
