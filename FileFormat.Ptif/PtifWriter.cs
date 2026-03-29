using System;
using System.Buffers.Binary;

namespace FileFormat.Ptif;

/// <summary>Assembles PTIF (Pyramid TIFF) file bytes. Produces a single-IFD uncompressed TIFF in little-endian byte order.</summary>
public static class PtifWriter {

  // TIFF tag IDs
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

  public static byte[] ToBytes(PtifFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _Assemble(file.PixelData, file.Width, file.Height, file.SamplesPerPixel, file.BitsPerSample);
  }

  internal static byte[] _Assemble(byte[] pixelData, int width, int height, int samplesPerPixel, int bitsPerSample) {
    // Layout:
    // [0..7]    TIFF header (8 bytes): byte order "II", magic 42, IFD offset
    // [8..N]    IFD: 2-byte count + entries (12 bytes each) + 4-byte next IFD offset (0)
    // [N..N+K]  Extra data for multi-value tags (BitsPerSample array when samples > 1)
    // [N+K..]   Pixel data (single strip)

    var ifdOffset = 8;
    var ifdSize = 2 + _IFD_ENTRY_COUNT * 12 + 4; // count + entries + next IFD
    var extraDataOffset = ifdOffset + ifdSize;

    // BitsPerSample needs external storage when samplesPerPixel > 2 (more than 4 bytes)
    var bpsExternalSize = samplesPerPixel > 2 ? samplesPerPixel * 2 : 0;
    var pixelDataOffset = extraDataOffset + bpsExternalSize;

    var bytesPerPixel = samplesPerPixel * (bitsPerSample / 8);
    var totalPixelBytes = width * height * bytesPerPixel;
    var fileSize = pixelDataOffset + totalPixelBytes;
    var result = new byte[fileSize];
    var span = result.AsSpan();

    // TIFF header
    result[0] = (byte)'I';
    result[1] = (byte)'I';
    BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 42);
    BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)ifdOffset);

    // IFD
    var pos = ifdOffset;
    BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], _IFD_ENTRY_COUNT);
    pos += 2;

    // Photometric: 1=BlackIsZero (grayscale), 2=RGB
    var photometric = (ushort)(samplesPerPixel == 1 ? 1 : 2);

    // Tags must be in ascending order per TIFF spec
    _WriteIfdEntry(span, ref pos, _TAG_IMAGE_WIDTH, _TYPE_LONG, 1, (uint)width);
    _WriteIfdEntry(span, ref pos, _TAG_IMAGE_LENGTH, _TYPE_LONG, 1, (uint)height);

    if (samplesPerPixel <= 2) {
      // Fits in 4-byte value field
      _WriteIfdEntry(span, ref pos, _TAG_BITS_PER_SAMPLE, _TYPE_SHORT, (uint)samplesPerPixel, (uint)bitsPerSample);
    } else {
      // Write offset to external data
      _WriteIfdEntry(span, ref pos, _TAG_BITS_PER_SAMPLE, _TYPE_SHORT, (uint)samplesPerPixel, (uint)extraDataOffset);
      for (var i = 0; i < samplesPerPixel; ++i)
        BinaryPrimitives.WriteUInt16LittleEndian(span[(extraDataOffset + i * 2)..], (ushort)bitsPerSample);
    }

    _WriteIfdEntry(span, ref pos, _TAG_COMPRESSION, _TYPE_SHORT, 1, 1); // Uncompressed
    _WriteIfdEntry(span, ref pos, _TAG_PHOTOMETRIC_INTERPRETATION, _TYPE_SHORT, 1, photometric);
    _WriteIfdEntry(span, ref pos, _TAG_STRIP_OFFSETS, _TYPE_LONG, 1, (uint)pixelDataOffset);
    _WriteIfdEntry(span, ref pos, _TAG_SAMPLES_PER_PIXEL, _TYPE_SHORT, 1, (uint)samplesPerPixel);
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
