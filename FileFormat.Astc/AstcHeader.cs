using FileFormat.Core;

namespace FileFormat.Astc;

/// <summary>The 16-byte header at the start of every ASTC file. Magic is little-endian uint32, dimensions are uint24 LE.</summary>
[GenerateSerializer]
public readonly partial record struct AstcHeader(
  uint Magic,
  byte BlockDimX,
  byte BlockDimY,
  byte BlockDimZ,
  int Width,
  int Height,
  int Depth
) {

 public const int StructSize = 16;
 public const uint MagicValue = 0x5CA1AB13;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<AstcHeader>();
}
