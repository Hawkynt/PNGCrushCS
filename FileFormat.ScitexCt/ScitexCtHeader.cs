using FileFormat.Core;

namespace FileFormat.ScitexCt;

/// <summary>The 80-byte ASCII header at the start of every Scitex CT file.</summary>
[GenerateSerializer(FillByte = 0x20)]
[HeaderFiller("Signature", 0, 2)]
[HeaderFiller("HeaderSize", 2, 6)]
[HeaderFiller("Padding", 74, 6)]
public readonly partial record struct ScitexCtHeader(
  [property: HeaderField(8, 6, AsciiEncoding = AsciiEncoding.Decimal)] int Width,
  [property: HeaderField(14, 6, AsciiEncoding = AsciiEncoding.Decimal)] int Height,
  [property: HeaderField(20, 2, AsciiEncoding = AsciiEncoding.Decimal)] ScitexCtColorMode ColorMode,
  [property: HeaderField(22, 2, AsciiEncoding = AsciiEncoding.Decimal)] int BitsPerComponent,
  [property: HeaderField(24, 2, AsciiEncoding = AsciiEncoding.Decimal)] int Units,
  [property: HeaderField(26, 6, AsciiEncoding = AsciiEncoding.Decimal)] int HResolution,
  [property: HeaderField(32, 6, AsciiEncoding = AsciiEncoding.Decimal)] int VResolution,
  [property: HeaderField(38, 36)] string Description
) {

  public const int StructSize = 80;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<ScitexCtHeader>();
}
