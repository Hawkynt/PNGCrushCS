using FileFormat.Core;

namespace FileFormat.Emf;

/// <summary>The 80-byte fixed portion of an EMR_STRETCHDIBITS record.</summary>
[GenerateSerializer]
[Filler(48, 8)]
public readonly partial record struct EmfStretchDiBitsRecord(
 uint RecordType,
 uint RecordSize,
 int BoundsLeft,
 int BoundsTop,
 int BoundsRight,
 int BoundsBottom,
 int XDest,
 int YDest,
 int XSrc,
 int YSrc,
 int CxSrc,
 int CySrc,
 [property: FieldOffset(56)] uint OffBmiSrc,
 uint CbBmiSrc,
 uint OffBitsSrc,
 uint CbBitsSrc,
 uint UsageSrc,
 uint DwRop
) {

 public const int StructSize = 80;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<EmfStretchDiBitsRecord>();
}
