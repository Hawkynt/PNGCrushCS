using FileFormat.Core;

namespace FileFormat.Xcursor;

/// <summary>Xcursor 36-byte image chunk header (LE).</summary>
[GenerateSerializer]
public readonly partial record struct XcursorImageChunkHeader(
  [property: HeaderField(0, 4)] uint ChunkHeaderSize,
  [property: HeaderField(4, 4)] uint ChunkType,
  [property: HeaderField(8, 4)] uint ChunkSubtype,
  [property: HeaderField(12, 4)] uint Version,
  [property: HeaderField(16, 4)] uint Width,
  [property: HeaderField(20, 4)] uint Height,
  [property: HeaderField(24, 4)] uint XHot,
  [property: HeaderField(28, 4)] uint YHot,
  [property: HeaderField(32, 4)] uint Delay
) {

  public const int StructSize = 36;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<XcursorImageChunkHeader>();
}
