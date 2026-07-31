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
  /// <summary>
  /// The machine dependency byte, which is where VIFF states its byte order: 0x02 is
  /// VFF_DEP_IEEEORDER (big-endian, what Sun, SGI and ImageMagick write), 0x04 is VFF_DEP_DECORDER
  /// (little-endian). The endian fields below keyed on 0x08 — VFF_DEP_NSORDER, for NS32000 machines
  /// — so every ordinary big-endian file was read the wrong way round and a 37x23 image came back as
  /// 620756992x385875968. This library's own writer emits 0x02, so it was producing files that
  /// contradicted their own header and that only it could read.
  /// </summary>
  [property: Field(4, 1)] byte MachineDep,
  [property: Field(8, 512)] string Comment,
  [property: Field(520, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint RowSize,
  [property: Field(524, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint ColSize,
  [property: Field(528, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint SubRowSize,
  [property: Field(532, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] float StartX,
  [property: Field(536, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] float StartY,
  [property: Field(540, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] float PixelSize,
  [property: Field(544, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint Location,
  [property: Field(548, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint Padding,
  [property: Field(552, 2, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] ushort FileSpare,
  [property: Field(556, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint MapType,
  [property: Field(560, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint MapRowSize,
  [property: Field(564, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint MapColSize,
  [property: Field(568, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint MapSubRowSize,
  [property: Field(572, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint MapStorageType,
  [property: Field(576, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint MapRowSizePad,
  [property: Field(580, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint MapEnable,
  [property: Field(584, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint MapsPerCycle,
  [property: Field(588, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint ColorSpaceModel,
  [property: Field(592, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint IsBand,
  [property: Field(596, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint DataStorageType,
  [property: Field(600, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint DataEncode,
  [property: Field(604, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] float MapScheme0,
  [property: Field(608, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] float MapScheme1
) {

  public const int StructSize = 1024;
  public const byte Magic = 0xAB;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<ViffHeader>();
}
