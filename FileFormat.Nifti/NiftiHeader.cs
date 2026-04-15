using FileFormat.Core;

namespace FileFormat.Nifti;

/// <summary>The 348-byte NIfTI v1 header.</summary>
[GenerateSerializer]
[Filler(4, 10)]
[Filler(14, 18)]
[Filler(32, 4)]
[Filler(36, 2)]
[Filler(38, 1)]
[Filler(39, 1)]
[Filler(140, 4)]
[Filler(144, 4)]
public readonly partial record struct NiftiHeader {

  public const int StructSize = 348;

  [Field(0, 4)] public int SizeOfHdr { get; init; }
  [Field(40, 16, Name = "Dim[8]", ArrayLength = 8)] public short[] Dim { get; init; }
  [Field(56, 4)] public float IntentP1 { get; init; }
  [Field(60, 4)] public float IntentP2 { get; init; }
  [Field(64, 4)] public float IntentP3 { get; init; }
  [Field(68, 2)] public short IntentCode { get; init; }
  [Field(70, 2)] public short Datatype { get; init; }
  [Field(72, 2)] public short Bitpix { get; init; }
  [Field(74, 2)] public short SliceStart { get; init; }
  [Field(76, 32, Name = "Pixdim[8]", ArrayLength = 8)] public float[] Pixdim { get; init; }
  [Field(108, 4)] public float VoxOffset { get; init; }
  [Field(112, 4)] public float SclSlope { get; init; }
  [Field(116, 4)] public float SclInter { get; init; }
  [Field(120, 2)] public short SliceEnd { get; init; }
  [Field(122, 1)] public byte SliceCode { get; init; }
  [Field(123, 1)] public byte XyztUnits { get; init; }
  [Field(124, 4)] public float CalMax { get; init; }
  [Field(128, 4)] public float CalMin { get; init; }
  [Field(132, 4)] public float SliceDuration { get; init; }
  [Field(136, 4)] public float TOffset { get; init; }
  [Field(148, 80)] public string Descrip { get; init; }
  [Field(228, 24)] public string AuxFile { get; init; }
  [Field(252, 2)] public short QformCode { get; init; }
  [Field(254, 2)] public short SformCode { get; init; }
  [Field(256, 4)] public float QuaternB { get; init; }
  [Field(260, 4)] public float QuaternC { get; init; }
  [Field(264, 4)] public float QuaternD { get; init; }
  [Field(268, 4)] public float QoffsetX { get; init; }
  [Field(272, 4)] public float QoffsetY { get; init; }
  [Field(276, 4)] public float QoffsetZ { get; init; }
  [Field(280, 16, Name = "SrowX[4]", ArrayLength = 4)] public float[] SrowX { get; init; }
  [Field(296, 16, Name = "SrowY[4]", ArrayLength = 4)] public float[] SrowY { get; init; }
  [Field(312, 16, Name = "SrowZ[4]", ArrayLength = 4)] public float[] SrowZ { get; init; }
  [Field(328, 16)] public string IntentName { get; init; }
  [Field(344, 4)] public string Magic { get; init; }

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<NiftiHeader>();
}
