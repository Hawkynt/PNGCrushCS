using System;
using System.Buffers.Binary;
using FileFormat.Bmp;

namespace FileFormat.Emf;

/// <summary>Assembles EMF file bytes from pixel data.</summary>
public static class EmfWriter {

  private const uint _EMR_HEADER = 1;
  private const uint _EMF_SIGNATURE = 0x464D4520;
  private const uint _EMF_VERSION = 0x00010000;
  private const int _HEADER_RECORD_SIZE = EmfHeaderRecord.StructSize;
  private const uint _EMR_STRETCHDIBITS = 81;
  private const uint _EMR_EOF = 14;
  private const int _EOF_RECORD_SIZE = 20;
  private const int _BITMAPINFOHEADER_SIZE = BitmapInfoHeader.StructSize;
  private const int _STRETCHDIBITS_FIXED_SIZE = EmfStretchDiBitsRecord.StructSize;

  public static byte[] ToBytes(EmfFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var srcStride = width * 3;
    var dstStride = (width * 3 + 3) & ~3;
    var dibPixelSize = dstStride * height;
    var stretchRecordSize = _STRETCHDIBITS_FIXED_SIZE + _BITMAPINFOHEADER_SIZE + dibPixelSize;
    var totalSize = _HEADER_RECORD_SIZE + stretchRecordSize + _EOF_RECORD_SIZE;

    var result = new byte[totalSize];
    var span = result.AsSpan();

    // --- EMR_HEADER record ---
    var headerRecord = new EmfHeaderRecord(
      RecordType: _EMR_HEADER,
      RecordSize: (uint)_HEADER_RECORD_SIZE,
      BoundsLeft: 0,
      BoundsTop: 0,
      BoundsRight: width - 1,
      BoundsBottom: height - 1,
      FrameLeft: 0,
      FrameTop: 0,
      FrameRight: (int)(width * 2540L / 96),
      FrameBottom: (int)(height * 2540L / 96),
      Signature: _EMF_SIGNATURE,
      Version: _EMF_VERSION,
      FileSize: (uint)totalSize,
      RecordCount: 3,
      NumHandles: 1,
      Reserved: 0
    );
    headerRecord.WriteTo(span);

    // --- EMR_STRETCHDIBITS record ---
    var stretchOffset = _HEADER_RECORD_SIZE;
    var stretchRecord = new EmfStretchDiBitsRecord(
      RecordType: _EMR_STRETCHDIBITS,
      RecordSize: (uint)stretchRecordSize,
      BoundsLeft: 0,
      BoundsTop: 0,
      BoundsRight: width - 1,
      BoundsBottom: height - 1,
      XDest: 0,
      YDest: 0,
      XSrc: 0,
      YSrc: 0,
      CxSrc: width,
      CySrc: height,
      OffBmiSrc: (uint)_STRETCHDIBITS_FIXED_SIZE,
      CbBmiSrc: (uint)_BITMAPINFOHEADER_SIZE,
      OffBitsSrc: (uint)(_STRETCHDIBITS_FIXED_SIZE + _BITMAPINFOHEADER_SIZE),
      CbBitsSrc: (uint)dibPixelSize,
      UsageSrc: 0,
      DwRop: 0x00CC0020
    );
    stretchRecord.WriteTo(span[stretchOffset..]);

    // BITMAPINFOHEADER
    var bmiOffset = stretchOffset + _STRETCHDIBITS_FIXED_SIZE;
    var bih = new BitmapInfoHeader(_BITMAPINFOHEADER_SIZE, width, height, 1, 24, 0, dibPixelSize, 0, 0, 0, 0);
    bih.WriteTo(span[bmiOffset..]);

    // Pixel data: convert top-down RGB24 to bottom-up BGR with padding
    var pixelOffset = bmiOffset + _BITMAPINFOHEADER_SIZE;
    for (var y = 0; y < height; ++y) {
      var srcRow = height - 1 - y; // bottom-up: first BMP row = last image row
      var srcOff = srcRow * srcStride;
      var dstOff = pixelOffset + y * dstStride;

      for (var x = 0; x < width; ++x) {
        var si = srcOff + x * 3;
        var di = dstOff + x * 3;
        // RGB to BGR
        result[di] = pixelData[si + 2];
        result[di + 1] = pixelData[si + 1];
        result[di + 2] = pixelData[si];
      }
    }

    // --- EMR_EOF record ---
    var eofOffset = stretchOffset + stretchRecordSize;
    var es = span[eofOffset..];
    BinaryPrimitives.WriteUInt32LittleEndian(es, _EMR_EOF);
    BinaryPrimitives.WriteUInt32LittleEndian(es[4..], (uint)_EOF_RECORD_SIZE);
    // nPalEntries = 0 (already zero)
    // offPalEntries = 0 (already zero)
    // nSizeLast = size of EOF record
    BinaryPrimitives.WriteUInt32LittleEndian(es[16..], (uint)_EOF_RECORD_SIZE);

    return result;
  }
}
