using System;
using System.Buffers.Binary;
using FileFormat.JpegXr.Codec;

namespace FileFormat.JpegXr;

/// <summary>Assembles JPEG XR file bytes using the TIFF-like container with II byte order and 0xBC01 magic.</summary>
public static class JpegXrWriter {

  /// <summary>Number of IFD entries we write: PixelFormat, ImageWidth, ImageHeight, ImageOffset, ImageByteCount.</summary>
  private const int _IFD_ENTRY_COUNT = 5;

  public static byte[] ToBytes(JpegXrFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _Assemble(file.PixelData, file.Width, file.Height, file.ComponentCount);
  }

  internal static byte[] _Assemble(byte[] pixelData, int width, int height, int componentCount) {
    // Encode pixel data using the JPEG XR codec
    var compressedData = JxrEncoder.Encode(pixelData, width, height, componentCount);

    // Layout:
    // [0..7]     Header (8 bytes): "II" + 0xBC01 magic + IFD offset
    // [8..N]     IFD: 2-byte count + entries (12 bytes each) + 4-byte next IFD offset (0)
    // [N..]      Compressed image data

    var ifdOffset = 8;
    var ifdSize = 2 + _IFD_ENTRY_COUNT * 12 + 4; // count + entries + next IFD
    var pixelDataOffset = ifdOffset + ifdSize;
    var totalPixelBytes = compressedData.Length;
    var fileSize = pixelDataOffset + totalPixelBytes;

    var result = new byte[fileSize];
    var span = result.AsSpan();

    // Header
    result[0] = (byte)'I';
    result[1] = (byte)'I';
    new JpegXrHeader(JpegXrReader.JPEGXR_MAGIC, (uint)ifdOffset).WriteTo(span);

    // IFD
    var pos = ifdOffset;
    BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], _IFD_ENTRY_COUNT);
    pos += 2;

    // Pixel format: use BYTE type with count=1 for simplified encoding
    var pixelFormatByte = componentCount == 1
      ? JpegXrIfd.PIXEL_FORMAT_8BPP_GRAY
      : JpegXrIfd.PIXEL_FORMAT_24BPP_RGB;

    // Tags must be in ascending order per TIFF convention
    JpegXrIfd.WriteEntry(span, ref pos, JpegXrIfd.TAG_PIXEL_FORMAT, JpegXrIfd.TYPE_BYTE, 1, pixelFormatByte);
    JpegXrIfd.WriteEntry(span, ref pos, JpegXrIfd.TAG_IMAGE_WIDTH, JpegXrIfd.TYPE_LONG, 1, (uint)width);
    JpegXrIfd.WriteEntry(span, ref pos, JpegXrIfd.TAG_IMAGE_HEIGHT, JpegXrIfd.TYPE_LONG, 1, (uint)height);
    JpegXrIfd.WriteEntry(span, ref pos, JpegXrIfd.TAG_IMAGE_OFFSET, JpegXrIfd.TYPE_LONG, 1, (uint)pixelDataOffset);
    JpegXrIfd.WriteEntry(span, ref pos, JpegXrIfd.TAG_IMAGE_BYTE_COUNT, JpegXrIfd.TYPE_LONG, 1, (uint)totalPixelBytes);

    // Next IFD offset = 0 (no more IFDs)
    BinaryPrimitives.WriteUInt32LittleEndian(span[pos..], 0);

    // Compressed image data
    compressedData.AsSpan(0, totalPixelBytes).CopyTo(result.AsSpan(pixelDataOffset));

    return result;
  }
}
