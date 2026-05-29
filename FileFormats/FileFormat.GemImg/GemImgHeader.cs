using FileFormat.Core;

namespace FileFormat.GemImg;

/// <summary>The 16-byte GEM IMG header at the start of every IMG file (big-endian).</summary>
[GenerateSerializer, Endian(Endianness.Big)]
public readonly partial record struct GemImgHeader(
  short Version,
  short HeaderLength,
  short NumPlanes,
  short PatternLength,
  short PixelWidth,
  short PixelHeight,
  short ScanWidth,
  short ScanLines
) {

 public const int StructSize = 16;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<GemImgHeader>();
}
