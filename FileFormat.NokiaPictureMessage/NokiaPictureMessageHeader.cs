using FileFormat.Core;

namespace FileFormat.NokiaPictureMessage;

/// <summary>The 4-byte header at the start of every NPM file: Type (0x00), Width, Height, Depth (0x01).</summary>
[GenerateSerializer]
public readonly partial record struct NokiaPictureMessageHeader(
  [property: HeaderField(0, 1)] byte Type,
  [property: HeaderField(1, 1)] byte Width,
  [property: HeaderField(2, 1)] byte Height,
  [property: HeaderField(3, 1)] byte Depth
) {

  public const int StructSize = 4;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<NokiaPictureMessageHeader>();
}
