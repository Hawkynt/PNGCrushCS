using FileFormat.Core;

namespace FileFormat.Cel;

/// <summary>The 32-byte header at the start of every KiSS CEL file.</summary>
[GenerateSerializer]
[HeaderFiller("Reserved0", 6, 2)]
[HeaderFiller("Reserved1", 24, 8)]
public readonly partial record struct CelHeader(
  [property: HeaderField(0, 4, Name = "Magic")] uint Magic,
  [property: HeaderField(4, 1)] byte Mark,
  [property: HeaderField(5, 1)] byte BitsPerPixel,
  [property: HeaderField(8, 4)] uint Width,
  [property: HeaderField(12, 4)] uint Height,
  [property: HeaderField(16, 4)] uint XOffset,
  [property: HeaderField(20, 4)] uint YOffset
) {

  public const int StructSize = 32;
  public const uint ExpectedMagic = 0x5353694B; // "KiSS" in little-endian

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<CelHeader>();
}
