using FileFormat.Core;

namespace FileFormat.MacPaint;

/// <summary>The 512-byte header at the start of every MacPaint file.</summary>
[GenerateSerializer]
[HeaderFiller("Padding", 4 + MacPaintHeader.PatternsSize, MacPaintHeader.PaddingSize)]
internal readonly partial record struct MacPaintHeader(
  [property: HeaderField(0, 4, Endianness = Endianness.Big)] int Version,
  [property: HeaderField(4, MacPaintHeader.PatternsSize)] byte[] Patterns
) {

  public const int StructSize = 512;
  public const int PatternsSize = 304;
  public const int PaddingSize = 204;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<MacPaintHeader>();
}
