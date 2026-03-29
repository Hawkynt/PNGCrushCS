using FileFormat.Core;

namespace FileFormat.Tim;

/// <summary>The 12-byte block header used for both CLUT and image data blocks in TIM files.</summary>
[GenerateSerializer]
public readonly partial record struct TimBlockHeader(
  [property: HeaderField(0, 4)] uint BlockSize,
  [property: HeaderField(4, 2)] ushort X,
  [property: HeaderField(6, 2)] ushort Y,
  [property: HeaderField(8, 2)] ushort Width,
  [property: HeaderField(10, 2)] ushort Height
) {

  public const int StructSize = 12;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<TimBlockHeader>();
}
