using FileFormat.Core;

namespace FileFormat.Viff;

/// <summary>
/// The 1024-byte header at the start of every VIFF file.
/// MachineDep 0x08 = big-endian (Sun), 0x02 = little-endian (DEC).
/// </summary>
[GenerateSerializer]
[HeaderFiller("Spare", 5, 3)]
[HeaderFiller("Alignment", 554, 2)]
[HeaderFiller("Reserved", 612, 412)]
public readonly partial record struct ViffHeader(
  [property: HeaderField(0, 1)] byte Identifier,
  [property: HeaderField(1, 1)] byte FileType,
  [property: HeaderField(2, 1)] byte Release,
  [property: HeaderField(3, 1)] byte Version,
  [property: HeaderField(4, 1)] byte MachineDep,
  [property: HeaderField(8, 512)] string Comment,
  [property: HeaderField(520, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint RowSize,
  [property: HeaderField(524, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint ColSize,
  [property: HeaderField(528, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint SubRowSize,
  [property: HeaderField(532, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] float StartX,
  [property: HeaderField(536, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] float StartY,
  [property: HeaderField(540, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] float PixelSize,
  [property: HeaderField(544, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint Location,
  [property: HeaderField(548, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint Padding,
  [property: HeaderField(552, 2, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] ushort FileSpare,
  [property: HeaderField(556, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint MapType,
  [property: HeaderField(560, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint MapRowSize,
  [property: HeaderField(564, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint MapColSize,
  [property: HeaderField(568, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint MapSubRowSize,
  [property: HeaderField(572, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint MapStorageType,
  [property: HeaderField(576, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint MapRowSizePad,
  [property: HeaderField(580, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint MapEnable,
  [property: HeaderField(584, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint MapsPerCycle,
  [property: HeaderField(588, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint ColorSpaceModel,
  [property: HeaderField(592, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint IsBand,
  [property: HeaderField(596, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint DataStorageType,
  [property: HeaderField(600, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint DataEncode,
  [property: HeaderField(604, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] float MapScheme0,
  [property: HeaderField(608, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] float MapScheme1
) {

  public const int StructSize = 1024;
  public const byte Magic = 0xAB;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<ViffHeader>();
}
