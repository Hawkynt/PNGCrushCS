using FileFormat.Core;

namespace FileFormat.Tim;

/// <summary>The 12-byte block header used for both CLUT and image data blocks in TIM files.</summary>
[GenerateSerializer]
public readonly partial record struct TimBlockHeader(
  uint BlockSize,
  ushort X,
  ushort Y,
  ushort Width,
  ushort Height
) {

 public const int StructSize = 12;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<TimBlockHeader>();
}
