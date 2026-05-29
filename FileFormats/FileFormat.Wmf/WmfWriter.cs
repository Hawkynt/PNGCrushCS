using System;
using FileFormat.Bmp;

namespace FileFormat.Wmf;

/// <summary>Assembles WMF file bytes from pixel data.</summary>
public static class WmfWriter {

  private const uint _PLACEABLE_MAGIC = 0x9AC6CDD7;
  private const ushort _META_STRETCHDIB = 0x0F43;
  private const int _BITMAPINFOHEADER_SIZE = 40;
  private const ushort _LOGICAL_UNITS_PER_INCH = 1440;

  public static byte[] ToBytes(WmfFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var stride = (width * 3 + 3) & ~3;
    var dibPixelSize = stride * height;
    var dibSize = _BITMAPINFOHEADER_SIZE + dibPixelSize;

    // META_STRETCHDIB record: size(4) + function(2) + rasterOp(4) + srcHeight(2) + srcWidth(2) + ySrc(2) + xSrc(2) + destHeight(2) + destWidth(2) + yDest(2) + xDest(2) + colorUse(2) + DIB
    // That's 14 words of params before the DIB = 28 bytes of params
    var stretchDibRecordSize = 6 + 22 + dibSize; // 6=size+function, 22=params before DIB
    var stretchDibRecordSizeInWords = (stretchDibRecordSize + 1) / 2;
    stretchDibRecordSize = stretchDibRecordSizeInWords * 2; // round up to word boundary

    // META_EOF record: 3 words = 6 bytes
    var eofRecordSize = 6;

    var totalRecordSize = stretchDibRecordSize + eofRecordSize;
    var totalFileSize = WmfPlaceableHeader.StructSize + WmfStandardHeader.StructSize + totalRecordSize;
    var result = new byte[totalFileSize];
    var span = result.AsSpan();

    // Placeable header: write with checksum=0 first, compute checksum, then re-write with correct value
    var placeable = new WmfPlaceableHeader(_PLACEABLE_MAGIC, 0, 0, 0, (short)width, (short)height, _LOGICAL_UNITS_PER_INCH, 0, 0);
    placeable.WriteTo(span);
    var checksum = WmfPlaceableHeader.ComputeChecksum(span[..WmfPlaceableHeader.StructSize]);
    placeable = placeable with { Checksum = checksum };
    placeable.WriteTo(span);

    // Standard WMF header
    var fileSizeInWords = (uint)((WmfStandardHeader.StructSize + totalRecordSize) / 2);
    var standard = new WmfStandardHeader(1, 9, 0x0300, fileSizeInWords, 0, (uint)stretchDibRecordSizeInWords, 0);
    standard.WriteTo(span[WmfPlaceableHeader.StructSize..]);

    // META_STRETCHDIB record
    var recOff = WmfPlaceableHeader.StructSize + WmfStandardHeader.StructSize;
    var stretchDib = new WmfStretchDibRecord(
      SizeInWords: (uint)stretchDibRecordSizeInWords,
      Function: _META_STRETCHDIB,
      RasterOp: 0x00CC0020,
      SrcHeight: (ushort)height,
      SrcWidth: (ushort)width,
      YSrc: 0,
      XSrc: 0,
      DestHeight: (ushort)height,
      DestWidth: (ushort)width,
      YDest: 0,
      XDest: 0,
      ColorUse: 0
    );
    stretchDib.WriteTo(span[recOff..]);

    // BITMAPINFOHEADER
    var bihOff = recOff + WmfStretchDibRecord.StructSize;
    var bih = new BitmapInfoHeader(_BITMAPINFOHEADER_SIZE, width, height, 1, 24, 0, 0, 0, 0, 0, 0);
    bih.WriteTo(span[bihOff..]);

    // Pixel data: convert from RGB24 top-down to BGR bottom-up with stride padding
    var pixOff = bihOff + _BITMAPINFOHEADER_SIZE;
    for (var y = 0; y < height; ++y) {
      var srcRow = height - 1 - y; // bottom-up
      var srcOffset = srcRow * width * 3;
      var dstOffset = pixOff + y * stride;
      for (var x = 0; x < width; ++x) {
        var si = srcOffset + x * 3;
        var di = dstOffset + x * 3;
        result[di] = pixelData.Length > si + 2 ? pixelData[si + 2] : (byte)0;     // B
        result[di + 1] = pixelData.Length > si + 1 ? pixelData[si + 1] : (byte)0; // G
        result[di + 2] = pixelData.Length > si ? pixelData[si] : (byte)0;         // R
      }
    }

    // META_EOF record
    var eofOff = recOff + stretchDibRecordSize;
    var eofRecord = new WmfEofRecord(SizeInWords: 3, Function: 0);
    eofRecord.WriteTo(span[eofOff..]);

    return result;
  }
}
