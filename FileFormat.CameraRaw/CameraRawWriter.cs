using System;
using System.Buffers.Binary;

namespace FileFormat.CameraRaw;

/// <summary>Assembles Camera RAW file bytes as a minimal TIFF-based container with uncompressed RGB pixel data.</summary>
public static class CameraRawWriter {

  // TIFF tag IDs (in ascending order per spec)
  private const ushort _TAG_IMAGE_WIDTH = 256;
  private const ushort _TAG_IMAGE_LENGTH = 257;
  private const ushort _TAG_BITS_PER_SAMPLE = 258;
  private const ushort _TAG_COMPRESSION = 259;
  private const ushort _TAG_PHOTOMETRIC_INTERPRETATION = 262;
  private const ushort _TAG_STRIP_OFFSETS = 273;
  private const ushort _TAG_SAMPLES_PER_PIXEL = 277;
  private const ushort _TAG_ROWS_PER_STRIP = 278;
  private const ushort _TAG_STRIP_BYTE_COUNTS = 279;

  private const ushort _TYPE_SHORT = 3;
  private const ushort _TYPE_LONG = 4;

  /// <summary>Number of IFD entries we write.</summary>
  private const int _IFD_ENTRY_COUNT = 9;

  public static byte[] ToBytes(CameraRawFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _Assemble(file.PixelData, file.Width, file.Height);
  }

  private static byte[] _Assemble(byte[] pixelData, int width, int height) {
    // Layout:
    // [0..7]    TIFF header (8 bytes): byte order "II", magic 42, IFD offset
    // [8..N]    IFD: 2-byte count + entries (12 bytes each) + 4-byte next IFD offset (0)
    // [N..N+6]  Extra data for BitsPerSample (3 x SHORT for RGB)
    // [N+6..]   Pixel data (single strip)

    const int samplesPerPixel = 3;
    const int bitsPerSample = 8;

    var ifdOffset = 8;
    var ifdSize = 2 + _IFD_ENTRY_COUNT * 12 + 4; // count + entries + next IFD
    var extraDataOffset = ifdOffset + ifdSize;

    // BitsPerSample needs external storage for 3 channels (3 * 2 = 6 bytes)
    var bpsExternalSize = samplesPerPixel * 2;
    var pixelDataOffset = extraDataOffset + bpsExternalSize;

    var totalPixelBytes = width * height * samplesPerPixel;
    var fileSize = pixelDataOffset + totalPixelBytes;
    var result = new byte[fileSize];
    var span = result.AsSpan();

    // TIFF header (little-endian)
    result[0] = (byte)'I';
    result[1] = (byte)'I';
    BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 42);
    BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)ifdOffset);

    // IFD
    var pos = ifdOffset;
    BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], _IFD_ENTRY_COUNT);
    pos += 2;

    // Tags in ascending tag-ID order
    _WriteIfdEntry(span, ref pos, _TAG_IMAGE_WIDTH, _TYPE_LONG, 1, (uint)width);
    _WriteIfdEntry(span, ref pos, _TAG_IMAGE_LENGTH, _TYPE_LONG, 1, (uint)height);

    // BitsPerSample: 3 x SHORT -> external data
    _WriteIfdEntry(span, ref pos, _TAG_BITS_PER_SAMPLE, _TYPE_SHORT, samplesPerPixel, (uint)extraDataOffset);
    for (var i = 0; i < samplesPerPixel; ++i)
      BinaryPrimitives.WriteUInt16LittleEndian(span[(extraDataOffset + i * 2)..], bitsPerSample);

    _WriteIfdEntry(span, ref pos, _TAG_COMPRESSION, _TYPE_SHORT, 1, 1); // Uncompressed
    _WriteIfdEntry(span, ref pos, _TAG_PHOTOMETRIC_INTERPRETATION, _TYPE_SHORT, 1, 2); // RGB
    _WriteIfdEntry(span, ref pos, _TAG_STRIP_OFFSETS, _TYPE_LONG, 1, (uint)pixelDataOffset);
    _WriteIfdEntry(span, ref pos, _TAG_SAMPLES_PER_PIXEL, _TYPE_SHORT, 1, samplesPerPixel);
    _WriteIfdEntry(span, ref pos, _TAG_ROWS_PER_STRIP, _TYPE_LONG, 1, (uint)height);
    _WriteIfdEntry(span, ref pos, _TAG_STRIP_BYTE_COUNTS, _TYPE_LONG, 1, (uint)totalPixelBytes);

    // Next IFD offset = 0 (no more IFDs)
    BinaryPrimitives.WriteUInt32LittleEndian(span[pos..], 0);

    // Pixel data
    pixelData.AsSpan(0, Math.Min(totalPixelBytes, pixelData.Length)).CopyTo(result.AsSpan(pixelDataOffset));

    return result;
  }

  private static void _WriteIfdEntry(Span<byte> span, ref int pos, ushort tag, ushort type, uint count, uint value) {
    BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], tag);
    BinaryPrimitives.WriteUInt16LittleEndian(span[(pos + 2)..], type);
    BinaryPrimitives.WriteUInt32LittleEndian(span[(pos + 4)..], count);
    if (type == _TYPE_SHORT && count == 1)
      BinaryPrimitives.WriteUInt16LittleEndian(span[(pos + 8)..], (ushort)value);
    else
      BinaryPrimitives.WriteUInt32LittleEndian(span[(pos + 8)..], value);
    pos += 12;
  }
}
