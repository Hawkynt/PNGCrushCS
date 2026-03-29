using FileFormat.Core;

namespace FileFormat.Emf;

/// <summary>The 80-byte fixed portion of an EMR_STRETCHDIBITS record.</summary>
[GenerateSerializer]
[HeaderFiller("Reserved", 48, 8)]
public readonly partial record struct EmfStretchDiBitsRecord(
  [property: HeaderField(0, 4)] uint RecordType,
  [property: HeaderField(4, 4)] uint RecordSize,
  [property: HeaderField(8, 4)] int BoundsLeft,
  [property: HeaderField(12, 4)] int BoundsTop,
  [property: HeaderField(16, 4)] int BoundsRight,
  [property: HeaderField(20, 4)] int BoundsBottom,
  [property: HeaderField(24, 4)] int XDest,
  [property: HeaderField(28, 4)] int YDest,
  [property: HeaderField(32, 4)] int XSrc,
  [property: HeaderField(36, 4)] int YSrc,
  [property: HeaderField(40, 4)] int CxSrc,
  [property: HeaderField(44, 4)] int CySrc,
  [property: HeaderField(56, 4)] uint OffBmiSrc,
  [property: HeaderField(60, 4)] uint CbBmiSrc,
  [property: HeaderField(64, 4)] uint OffBitsSrc,
  [property: HeaderField(68, 4)] uint CbBitsSrc,
  [property: HeaderField(72, 4)] uint UsageSrc,
  [property: HeaderField(76, 4)] uint DwRop
) {

  public const int StructSize = 80;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<EmfStretchDiBitsRecord>();
}
