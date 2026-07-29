using FileFormat.Core;

namespace FileFormat.Fli;

/// <summary>The 6-byte chunk header within a FLI/FLC frame: size(4) + type(2).</summary>
[GenerateSerializer]
public readonly partial record struct FliChunkHeader(
  int Size,
  short Type
) {

 public const int StructSize = 6;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<FliChunkHeader>();
}
