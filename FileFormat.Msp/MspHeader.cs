using FileFormat.Core;

namespace FileFormat.Msp;

/// <summary>The 32-byte header at the start of every MSP file. All fields are little-endian uint16.</summary>
[GenerateSerializer]
public readonly partial record struct MspHeader(
  [property: HeaderField(0, 2)] ushort Key1,
  [property: HeaderField(2, 2)] ushort Key2,
  [property: HeaderField(4, 2)] ushort Width,
  [property: HeaderField(6, 2)] ushort Height,
  [property: HeaderField(8, 2)] ushort XAspect,
  [property: HeaderField(10, 2)] ushort YAspect,
  [property: HeaderField(12, 2)] ushort XAspectPrinter,
  [property: HeaderField(14, 2)] ushort YAspectPrinter,
  [property: HeaderField(16, 2)] ushort PrinterWidth,
  [property: HeaderField(18, 2)] ushort PrinterHeight,
  [property: HeaderField(20, 2)] ushort XAspectCorr,
  [property: HeaderField(22, 2)] ushort YAspectCorr,
  [property: HeaderField(24, 2)] ushort Checksum,
  [property: HeaderField(26, 2)] ushort Padding1,
  [property: HeaderField(28, 2)] ushort Padding2,
  [property: HeaderField(30, 2)] ushort Padding3
) {

  public const int StructSize = 32;

  public const ushort V1Key1 = 0x6144;
  public const ushort V1Key2 = 0x4D6E;
  public const ushort V2Key1 = 0x694C;
  public const ushort V2Key2 = 0x536E;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<MspHeader>();
}
