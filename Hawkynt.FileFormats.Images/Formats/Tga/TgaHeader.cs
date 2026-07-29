using FileFormat.Core;

namespace FileFormat.Tga;

/// <summary>The 18-byte header at the start of every TGA file.</summary>
[GenerateSerializer]
internal readonly partial record struct TgaHeader(
  byte IdLength,
  byte ColorMapType,
  byte ImageType,
  short ColorMapFirstEntry,
  short ColorMapLength,
  byte ColorMapEntrySize,
  short XOrigin,
  short YOrigin,
  short Width,
  short Height,
  byte BitsPerPixel,
  byte ImageDescriptor
) {

 public const int StructSize = 18;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<TgaHeader>();
}
