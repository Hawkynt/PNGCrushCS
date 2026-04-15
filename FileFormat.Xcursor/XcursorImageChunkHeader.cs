using FileFormat.Core;

namespace FileFormat.Xcursor;

/// <summary>Xcursor 36-byte image chunk header (LE).</summary>
[GenerateSerializer]
public readonly partial record struct XcursorImageChunkHeader(
  uint ChunkHeaderSize,
  uint ChunkType,
  uint ChunkSubtype,
  uint Version,
  uint Width,
  uint Height,
  uint XHot,
  uint YHot,
  uint Delay
) {

 public const int StructSize = 36;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<XcursorImageChunkHeader>();
}
