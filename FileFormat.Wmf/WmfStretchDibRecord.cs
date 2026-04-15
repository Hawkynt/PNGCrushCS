using FileFormat.Core;

namespace FileFormat.Wmf;

/// <summary>The 28-byte META_STRETCHDIB record header (size + function + parameters before the embedded DIB).</summary>
[GenerateSerializer]
internal readonly partial record struct WmfStretchDibRecord(
  uint SizeInWords,
  ushort Function,
  uint RasterOp,
  ushort SrcHeight,
  ushort SrcWidth,
  ushort YSrc,
  ushort XSrc,
  ushort DestHeight,
  ushort DestWidth,
  ushort YDest,
  ushort XDest,
  ushort ColorUse
) {

 public const int StructSize = 28;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<WmfStretchDibRecord>();
}
