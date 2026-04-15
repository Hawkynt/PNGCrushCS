using FileFormat.Core;

namespace FileFormat.Fli;

/// <summary>The 16-byte frame header within a FLI/FLC file: size(4) + magic(2) + chunks(2) + reserved(8).</summary>
[GenerateSerializer]
[Filler(8, 8)]
public readonly partial record struct FliFrameHeader(
  int Size,
  short Magic,
  short Chunks
) {

 public const int StructSize = 16;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<FliFrameHeader>();
}
