using FileFormat.Core;

namespace FileFormat.AmigaIcon;

/// <summary>
///   The 78-byte DiskObject header at the start of every Amiga .info file.
///   Only the fields relevant for image extraction are exposed; remaining bytes are preserved via filler entries.
/// </summary>
[GenerateSerializer]
[HeaderFiller("GadgetRemainder", 22, 32)]
[HeaderFiller("Padding55", 55, 1)]
[HeaderFiller("DefaultTool", 56, 4)]
[HeaderFiller("ToolTypes", 60, 4)]
[HeaderFiller("CurrentX", 64, 4)]
[HeaderFiller("CurrentY", 68, 4)]
[HeaderFiller("DrawerData", 72, 4)]
[HeaderFiller("StackSize", 76, 2)]
public readonly partial record struct AmigaIconHeader {

  public const int StructSize = 78;
  public const ushort MagicValue = 0xE310;

  [HeaderField(0, 2, Endianness = Endianness.Big)] public ushort Magic { get; init; }
  [HeaderField(2, 2, Endianness = Endianness.Big)] public ushort Version { get; init; }
  [HeaderField(4, 2, Endianness = Endianness.Big)] public ushort NextGadget { get; init; }
  [HeaderField(6, 2, Endianness = Endianness.Big)] public short LeftEdge { get; init; }
  [HeaderField(8, 2, Endianness = Endianness.Big)] public short TopEdge { get; init; }
  [HeaderField(10, 2, Endianness = Endianness.Big)] public short Width { get; init; }
  [HeaderField(12, 2, Endianness = Endianness.Big)] public short Height { get; init; }
  [HeaderField(14, 2, Endianness = Endianness.Big)] public short Depth { get; init; }
  [HeaderField(16, 2, Endianness = Endianness.Big)] public ushort ImageDataPointer { get; init; }
  [HeaderField(18, 2, Endianness = Endianness.Big)] public ushort PlanePick { get; init; }
  [HeaderField(20, 2, Endianness = Endianness.Big)] public ushort PlaneOnOff { get; init; }
  [HeaderField(54, 1, Name = "IconType")] public byte IconTypeByte { get; init; }

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<AmigaIconHeader>();
}
