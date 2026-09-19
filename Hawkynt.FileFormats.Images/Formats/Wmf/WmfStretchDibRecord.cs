using FileFormat.Core;

namespace FileFormat.Wmf;

/// <summary>The 28-byte META_STRETCHDIB record header (size + function + parameters before the embedded DIB).</summary>
/// <remarks>
/// The parameter order is the one MS-WMF 2.3.1.6 states, and it is not the order the field names
/// suggest: <c>ColorUsage</c> comes straight after the raster operation, ahead of the source
/// rectangle, not at the end beside it. It used to be written last here, which shifted every
/// parameter after the raster operation by one word — a player read the destination width out of
/// the field holding <c>yDst</c>, got zero, and drew a rectangle of no width. Our own reader never
/// noticed because it skips a fixed twenty-eight bytes to reach the DIB and reads none of these.
/// </remarks>
[GenerateSerializer]
internal readonly partial record struct WmfStretchDibRecord(
  uint SizeInWords,
  ushort Function,
  uint RasterOp,
  ushort ColorUse,
  ushort SrcHeight,
  ushort SrcWidth,
  ushort YSrc,
  ushort XSrc,
  ushort DestHeight,
  ushort DestWidth,
  ushort YDest,
  ushort XDest
) {

 public const int StructSize = 28;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<WmfStretchDibRecord>();
}
