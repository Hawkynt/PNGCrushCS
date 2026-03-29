using FileFormat.Core;

namespace FileFormat.Astc;

/// <summary>The 16-byte header at the start of every ASTC file. Magic is little-endian uint32, dimensions are uint24 LE.</summary>
[GenerateSerializer]
public readonly partial record struct AstcHeader(
  [property: HeaderField(0, 4)] uint Magic,
  [property: HeaderField(4, 1)] byte BlockDimX,
  [property: HeaderField(5, 1)] byte BlockDimY,
  [property: HeaderField(6, 1)] byte BlockDimZ,
  [property: HeaderField(7, 3)] int Width,
  [property: HeaderField(10, 3)] int Height,
  [property: HeaderField(13, 3)] int Depth
) {

  public const int StructSize = 16;
  public const uint MagicValue = 0x5CA1AB13;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<AstcHeader>();
}
