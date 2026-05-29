using FileFormat.Core;

namespace FileFormat.ScitexCt;

/// <summary>The 80-byte ASCII header at the start of every Scitex CT file.</summary>
[GenerateSerializer(FillByte = 0x20)]
[Filler(0, 2)]
[Filler(2, 6)]
[Filler(74, 6)]
public readonly partial record struct ScitexCtHeader(
  [property: Field(8, 6), TypeOverride(WireType.DecimalString)] int Width,
  [property: Field(14, 6), TypeOverride(WireType.DecimalString)] int Height,
  [property: Field(20, 2), TypeOverride(WireType.DecimalString)] ScitexCtColorMode ColorMode,
  [property: Field(22, 2), TypeOverride(WireType.DecimalString)] int BitsPerComponent,
  [property: Field(24, 2), TypeOverride(WireType.DecimalString)] int Units,
  [property: Field(26, 6), TypeOverride(WireType.DecimalString)] int HResolution,
  [property: Field(32, 6), TypeOverride(WireType.DecimalString)] int VResolution,
  [property: Field(38, 36), String] string Description
) {

  public const int StructSize = 80;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<ScitexCtHeader>();
}
