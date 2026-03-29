using FileFormat.Core;

namespace FileFormat.Nifti;

/// <summary>The 348-byte NIfTI v1 header.</summary>
[GenerateSerializer]
[HeaderFiller("DataType (legacy)", 4, 10)]
[HeaderFiller("DbName (legacy)", 14, 18)]
[HeaderFiller("Extents", 32, 4)]
[HeaderFiller("SessionError", 36, 2)]
[HeaderFiller("Regular", 38, 1)]
[HeaderFiller("DimInfo", 39, 1)]
[HeaderFiller("Glmax", 140, 4)]
[HeaderFiller("Glmin", 144, 4)]
public readonly partial record struct NiftiHeader {

  public const int StructSize = 348;

  [HeaderField(0, 4)] public int SizeOfHdr { get; init; }
  [HeaderField(40, 16, Name = "Dim[8]", ArrayLength = 8)] public short[] Dim { get; init; }
  [HeaderField(56, 4)] public float IntentP1 { get; init; }
  [HeaderField(60, 4)] public float IntentP2 { get; init; }
  [HeaderField(64, 4)] public float IntentP3 { get; init; }
  [HeaderField(68, 2)] public short IntentCode { get; init; }
  [HeaderField(70, 2)] public short Datatype { get; init; }
  [HeaderField(72, 2)] public short Bitpix { get; init; }
  [HeaderField(74, 2)] public short SliceStart { get; init; }
  [HeaderField(76, 32, Name = "Pixdim[8]", ArrayLength = 8)] public float[] Pixdim { get; init; }
  [HeaderField(108, 4)] public float VoxOffset { get; init; }
  [HeaderField(112, 4)] public float SclSlope { get; init; }
  [HeaderField(116, 4)] public float SclInter { get; init; }
  [HeaderField(120, 2)] public short SliceEnd { get; init; }
  [HeaderField(122, 1)] public byte SliceCode { get; init; }
  [HeaderField(123, 1)] public byte XyztUnits { get; init; }
  [HeaderField(124, 4)] public float CalMax { get; init; }
  [HeaderField(128, 4)] public float CalMin { get; init; }
  [HeaderField(132, 4)] public float SliceDuration { get; init; }
  [HeaderField(136, 4)] public float TOffset { get; init; }
  [HeaderField(148, 80)] public string Descrip { get; init; }
  [HeaderField(228, 24)] public string AuxFile { get; init; }
  [HeaderField(252, 2)] public short QformCode { get; init; }
  [HeaderField(254, 2)] public short SformCode { get; init; }
  [HeaderField(256, 4)] public float QuaternB { get; init; }
  [HeaderField(260, 4)] public float QuaternC { get; init; }
  [HeaderField(264, 4)] public float QuaternD { get; init; }
  [HeaderField(268, 4)] public float QoffsetX { get; init; }
  [HeaderField(272, 4)] public float QoffsetY { get; init; }
  [HeaderField(276, 4)] public float QoffsetZ { get; init; }
  [HeaderField(280, 16, Name = "SrowX[4]", ArrayLength = 4)] public float[] SrowX { get; init; }
  [HeaderField(296, 16, Name = "SrowY[4]", ArrayLength = 4)] public float[] SrowY { get; init; }
  [HeaderField(312, 16, Name = "SrowZ[4]", ArrayLength = 4)] public float[] SrowZ { get; init; }
  [HeaderField(328, 16)] public string IntentName { get; init; }
  [HeaderField(344, 4)] public string Magic { get; init; }

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<NiftiHeader>();
}
