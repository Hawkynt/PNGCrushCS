using FileFormat.Core;

namespace FileFormat.Viff;

/// <summary>
/// The 1024-byte header at the start of every VIFF file.
/// MachineDep 0x08 = big-endian (Sun), 0x02 = little-endian (DEC).
/// </summary>
[GenerateSerializer]
[Filler(5, 3)]
[Filler(554, 2)]
[Filler(612, 412)]
public readonly partial record struct ViffHeader(
  [property: Field(0, 1)] byte Identifier,
  [property: Field(1, 1)] byte FileType,
  [property: Field(2, 1)] byte Release,
  [property: Field(3, 1)] byte Version,
  [property: Field(4, 1)] byte MachineDep,
  [property: Field(8, 512)] string Comment,
  [property: Field(520, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint RowSize,
  [property: Field(524, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint ColSize,
  [property: Field(528, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint SubRowSize,
  [property: Field(532, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] float StartX,
  [property: Field(536, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] float StartY,
  [property: Field(540, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] float PixelSize,
  [property: Field(544, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint Location,
  [property: Field(548, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint Padding,
  [property: Field(552, 2, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] ushort FileSpare,
  [property: Field(556, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint MapType,
  [property: Field(560, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint MapRowSize,
  [property: Field(564, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint MapColSize,
  [property: Field(568, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint MapSubRowSize,
  [property: Field(572, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint MapStorageType,
  [property: Field(576, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint MapRowSizePad,
  [property: Field(580, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint MapEnable,
  [property: Field(584, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint MapsPerCycle,
  [property: Field(588, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint ColorSpaceModel,
  [property: Field(592, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint IsBand,
  [property: Field(596, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint DataStorageType,
  [property: Field(600, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] uint DataEncode,
  [property: Field(604, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] float MapScheme0,
  [property: Field(608, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x08)] float MapScheme1
) {

  public const int StructSize = 1024;
  public const byte Magic = 0xAB;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<ViffHeader>();
}
