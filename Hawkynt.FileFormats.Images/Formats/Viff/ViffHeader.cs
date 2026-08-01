using FileFormat.Core;

namespace FileFormat.Viff;

/// <summary>
/// The 1024-byte header at the start of every VIFF file.
/// MachineDep 0x02 = big-endian (IEEE order), 0x04 = little-endian (DEC order).
/// </summary>
/// <remarks>
/// The field order below is Khoros' <c>xvimage</c> struct verbatim, which is the only thing that
/// makes a VIFF written elsewhere readable here. It had drifted: one <c>PixelSize</c> float stood
/// where the struct has the two-float pair <c>pixsizx</c>/<c>pixsizy</c>, so every field from offset
/// 544 onwards was read four bytes early, and the band count and storage type — the two that decide
/// what the pixels even mean — came from <c>subrow_size</c> and <c>maps_per_cycle</c> instead of
/// <c>num_data_bands</c> and <c>data_storage_type</c>. The writer put them back in the same wrong
/// places, so a round-trip through this library agreed with itself while an ImageMagick file came
/// back as a 1-bit bitmap: its <c>subrow_size</c> is 0, which the reader floored to one band, and its
/// <c>maps_per_cycle</c> is 0, which is <see cref="ViffStorageType.Bit"/>.
/// </remarks>
[GenerateSerializer]
[Filler(5, 3)]
[Filler(620, 404)]
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
  /// <summary>Pixels across. VIFF calls the horizontal extent a "row size", not a column count.</summary>
  [property: Field(520, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint RowSize,
  /// <summary>Pixels down.</summary>
  [property: Field(524, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint ColSize,
  /// <summary>Elements per pixel for vector data. Zero for an ordinary image — this is not the band count.</summary>
  [property: Field(528, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint SubRowSize,
  [property: Field(532, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] int StartX,
  [property: Field(536, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] int StartY,
  [property: Field(540, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] float PixelSizeX,
  [property: Field(544, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] float PixelSizeY,
  /// <summary>
  /// VFF_LOC_IMPLICIT (1) means the pixels lie on a regular grid, which is the only arrangement an
  /// image reader can use. ImageMagick refuses anything else outright, so a writer that leaves this
  /// at zero produces files it will not open.
  /// </summary>
  [property: Field(548, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint LocationType,
  [property: Field(552, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint LocationDim,
  [property: Field(556, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint NumberOfImages,
  /// <summary>Bands per pixel: 1 for greyscale or paletted, 3 for RGB. Stored band-sequentially.</summary>
  [property: Field(560, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint NumberDataBands,
  /// <summary>The element type of the pixel data — see <see cref="ViffStorageType"/>.</summary>
  [property: Field(564, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint DataStorageType,
  [property: Field(568, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint DataEncodeScheme,
  /// <summary>Whether a colour map applies, and how — see <see cref="ViffMapScheme"/>.</summary>
  [property: Field(572, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint MapScheme,
  /// <summary>The element type of the colour map — see <see cref="ViffMapType"/>.</summary>
  [property: Field(576, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint MapStorageType,
  /// <summary>Channels in the colour map: 3 for an RGB palette.</summary>
  [property: Field(580, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint MapRowSize,
  /// <summary>Entries in the colour map.</summary>
  [property: Field(584, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint MapColSize,
  [property: Field(588, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint MapSubRowSize,
  [property: Field(592, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint MapEnable,
  [property: Field(596, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint MapsPerCycle,
  [property: Field(600, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint ColorSpaceModel,
  [property: Field(604, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint ISpare1,
  [property: Field(608, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] uint ISpare2,
  [property: Field(612, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] float FSpare1,
  [property: Field(616, 4, EndianFieldName = "MachineDep", EndianComputeValue = 0x02)] float FSpare2
) {

  public const int StructSize = 1024;
  public const byte Magic = 0xAB;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<ViffHeader>();
}
