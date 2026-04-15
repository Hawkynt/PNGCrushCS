using FileFormat.Core;

namespace FileFormat.AmigaIcon;

/// <summary>
/// The 78-byte DiskObject header at the start of every Amiga .info file.
/// Only the fields relevant for image extraction are exposed; remaining bytes are preserved via filler entries.
/// </summary>
[GenerateSerializer, Endian(Endianness.Big)]
[Filler(22, 32)]
[Filler(55, 1)]
[Filler(56, 4)]
[Filler(60, 4)]
[Filler(64, 4)]
[Filler(68, 4)]
[Filler(72, 4)]
[Filler(76, 2)]
public readonly partial record struct AmigaIconHeader {

 public const int StructSize = 78;
 public const ushort MagicValue = 0xE310;

 [Field(0, 2, Endianness = Endianness.Big)] public ushort Magic { get; init; }
 [Field(2, 2, Endianness = Endianness.Big)] public ushort Version { get; init; }
 [Field(4, 2, Endianness = Endianness.Big)] public ushort NextGadget { get; init; }
 [Field(6, 2, Endianness = Endianness.Big)] public short LeftEdge { get; init; }
 [Field(8, 2, Endianness = Endianness.Big)] public short TopEdge { get; init; }
 [Field(10, 2, Endianness = Endianness.Big)] public short Width { get; init; }
 [Field(12, 2, Endianness = Endianness.Big)] public short Height { get; init; }
 [Field(14, 2, Endianness = Endianness.Big)] public short Depth { get; init; }
 [Field(16, 2, Endianness = Endianness.Big)] public ushort ImageDataPointer { get; init; }
 [Field(18, 2, Endianness = Endianness.Big)] public ushort PlanePick { get; init; }
 [Field(20, 2, Endianness = Endianness.Big)] public ushort PlaneOnOff { get; init; }
 [Field(54, 1, Name = "IconType")] public byte IconTypeByte { get; init; }

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<AmigaIconHeader>();
}
