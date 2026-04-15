using FileFormat.Core;

namespace FileFormat.Cel;

/// <summary>The 32-byte header at the start of every KiSS CEL file.</summary>
[GenerateSerializer]
[Filler(6, 2)]
[Filler(24, 8)]
public readonly partial record struct CelHeader(
  uint Magic,
  byte Mark,
  byte BitsPerPixel,
  [property: FieldOffset(8)] uint Width,
  uint Height,
  uint XOffset,
  uint YOffset
) {

  public const int StructSize = 32;
  public const uint ExpectedMagic = 0x5353694B; // "KiSS" in little-endian

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<CelHeader>();
}
