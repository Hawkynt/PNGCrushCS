using FileFormat.Core;

namespace FileFormat.Tga;

/// <summary>The 18-byte header at the start of every TGA file.</summary>
[GenerateSerializer]
internal readonly partial record struct TgaHeader(
  [property: HeaderField(0, 1)] byte IdLength,
  [property: HeaderField(1, 1)] byte ColorMapType,
  [property: HeaderField(2, 1)] byte ImageType,
  [property: HeaderField(3, 2)] short ColorMapFirstEntry,
  [property: HeaderField(5, 2)] short ColorMapLength,
  [property: HeaderField(7, 1)] byte ColorMapEntrySize,
  [property: HeaderField(8, 2)] short XOrigin,
  [property: HeaderField(10, 2)] short YOrigin,
  [property: HeaderField(12, 2)] short Width,
  [property: HeaderField(14, 2)] short Height,
  [property: HeaderField(16, 1)] byte BitsPerPixel,
  [property: HeaderField(17, 1)] byte ImageDescriptor
) {

  public const int StructSize = 18;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<TgaHeader>();
}
