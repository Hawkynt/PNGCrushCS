using FileFormat.Core;

namespace FileFormat.Msp;

/// <summary>The 32-byte header at the start of every MSP file. All fields are little-endian uint16.</summary>
[GenerateSerializer]
public readonly partial record struct MspHeader(
  ushort Key1,
  ushort Key2,
  ushort Width,
  ushort Height,
  ushort XAspect,
  ushort YAspect,
  ushort XAspectPrinter,
  ushort YAspectPrinter,
  ushort PrinterWidth,
  ushort PrinterHeight,
  ushort XAspectCorr,
  ushort YAspectCorr,
  ushort Checksum,
  ushort Padding1,
  ushort Padding2,
  ushort Padding3
) {

 public const int StructSize = 32;

 public const ushort V1Key1 = 0x6144;
 public const ushort V1Key2 = 0x4D6E;
 public const ushort V2Key1 = 0x694C;
 public const ushort V2Key2 = 0x536E;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<MspHeader>();
}
