using FileFormat.Core;

namespace FileFormat.Viff;

/// <summary>The 1024-byte header at the start of every VIFF file.</summary>
/// <remarks>
/// One byte says which way round the rest of it is, and it was read the wrong way round: 0x02 is
/// IEEE order, which is big-endian, and 0x04 is DEC order, which is little. This had 0x08 standing
/// for big — so every file written by anything, all of which say 0x02, was read byte-reversed from
/// the sizes down. The sizes themselves came out in the hundreds of millions and the rest followed.
/// </remarks>
[GenerateSerializer]
[Filler(5, 3)]
[Filler(612, 412)]
public readonly partial record struct ViffHeader(
  [property: Field(0, 1)] byte Identifier,
  [property: Field(1, 1)] byte FileType,
  [property: Field(2, 1)] byte Release,
  [property: Field(3, 1)] byte Version,
  [property: Field(4, 1)] byte MachineDep,
  [property: Field(8, 512)] string Comment,
  [property: Field(520, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] uint RowSize,
  [property: Field(524, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] uint ColSize,
  [property: Field(528, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] uint SubRowSize,
  [property: Field(532, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] float StartX,
  [property: Field(536, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] float StartY,
  [property: Field(540, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] float PixelSizeX,
  [property: Field(544, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] float PixelSizeY,
  [property: Field(548, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] uint LocationType,
  [property: Field(552, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] uint LocationDim,
  [property: Field(556, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] uint NumberOfImages,
  [property: Field(560, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] uint NumDataBands,
  [property: Field(564, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] uint DataStorageType,
  [property: Field(568, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] uint DataEncodeScheme,
  [property: Field(572, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] uint MapScheme,
  [property: Field(576, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] uint MapStorageType,
  [property: Field(580, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] uint MapRowSize,
  [property: Field(584, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] uint MapColSize,
  [property: Field(588, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] uint MapSubRowSize,
  [property: Field(592, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] uint MapEnable,
  [property: Field(596, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] uint MapsPerCycle,
  [property: Field(600, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] uint ColorSpaceModel,
  [property: Field(604, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] uint SpareInt1,
  [property: Field(608, 4, EndianFieldName = "MachineDep", EndianComputeValue = ViffHeader.IeeeByteOrder)] uint SpareInt2
) {

  public const int StructSize = 1024;

  /// <summary>IEEE byte order, which is big-endian, and what a file is written in.</summary>
  public const byte IeeeByteOrder = 0x02;

  /// <summary>DEC byte order, which is little-endian.</summary>
  public const byte DecByteOrder = 0x04;
  public const byte Magic = 0xAB;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<ViffHeader>();
}
