using System;
using System.Buffers.Binary;
using System.Text;

namespace FileFormat.Dng;

/// <summary>Assembles DNG (Adobe Digital Negative) file bytes. Produces a minimal single-IFD uncompressed DNG in little-endian byte order.</summary>
public static class DngWriter {

  // TIFF tag IDs (must be written in ascending order)
  private const ushort _TAG_IMAGE_WIDTH = 256;
  private const ushort _TAG_IMAGE_LENGTH = 257;
  private const ushort _TAG_BITS_PER_SAMPLE = 258;
  private const ushort _TAG_COMPRESSION = 259;
  private const ushort _TAG_PHOTOMETRIC_INTERPRETATION = 262;
  private const ushort _TAG_STRIP_OFFSETS = 273;
  private const ushort _TAG_SAMPLES_PER_PIXEL = 277;
  private const ushort _TAG_ROWS_PER_STRIP = 278;
  private const ushort _TAG_STRIP_BYTE_COUNTS = 279;
  private const ushort _TAG_DNG_VERSION = 50706;
  private const ushort _TAG_DNG_BACKWARD_VERSION = 50707;
  private const ushort _TAG_UNIQUE_CAMERA_MODEL = 50708;

  private const ushort _TYPE_BYTE = 1;
  private const ushort _TYPE_ASCII = 2;
  private const ushort _TYPE_SHORT = 3;
  private const ushort _TYPE_LONG = 4;

  /// <summary>Number of IFD entries we write.</summary>
  private const int _IFD_ENTRY_COUNT = 12;

  public static byte[] ToBytes(DngFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var cameraModelBytes = Encoding.ASCII.GetBytes(file.CameraModel + '\0');
    var samplesPerPixel = file.SamplesPerPixel;
    var bitsPerSample = file.BitsPerSample;
    var pixelData = file.PixelData;
    var width = file.Width;
    var height = file.Height;

    // Layout:
    // [0..7]    TIFF header (8 bytes)
    // [8..N]    IFD: 2-byte count + entries (12 bytes each) + 4-byte next IFD offset (0)
    // [N..N+K]  Extra data: BitsPerSample array (if needed), DNG version, backward version, camera model
    // [N+K..]   Pixel data (single strip)

    var ifdOffset = 8;
    var ifdSize = 2 + _IFD_ENTRY_COUNT * 12 + 4;
    var extraDataOffset = ifdOffset + ifdSize;

    // BitsPerSample needs external storage when samplesPerPixel > 2
    var bpsExternalSize = samplesPerPixel > 2 ? samplesPerPixel * 2 : 0;

    // DNG version + backward version always stored externally (4 bytes each, but BYTE type fits in value field)
    // Camera model stored externally if > 4 bytes
    var cameraModelExternalSize = cameraModelBytes.Length > 4 ? cameraModelBytes.Length : 0;

    // Align to word boundary if needed
    var totalExtraSize = bpsExternalSize + cameraModelExternalSize;
    var pixelDataOffset = extraDataOffset + totalExtraSize;
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

    // Track where extra data goes
    var currentExtraOffset = extraDataOffset;

    // IFD
    var pos = ifdOffset;
    BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], _IFD_ENTRY_COUNT);
    pos += 2;

    // Photometric
    var photometric = (ushort)file.Photometric;
    if (photometric == 0)
      photometric = (ushort)(samplesPerPixel == 1 ? 1 : 2);

    // Tags must be in ascending order per TIFF spec
    _WriteIfdEntry(span, ref pos, _TAG_IMAGE_WIDTH, _TYPE_LONG, 1, (uint)width);
    _WriteIfdEntry(span, ref pos, _TAG_IMAGE_LENGTH, _TYPE_LONG, 1, (uint)height);

    if (samplesPerPixel <= 2) {
      _WriteIfdEntry(span, ref pos, _TAG_BITS_PER_SAMPLE, _TYPE_SHORT, (uint)samplesPerPixel, (uint)bitsPerSample);
    } else {
      _WriteIfdEntry(span, ref pos, _TAG_BITS_PER_SAMPLE, _TYPE_SHORT, (uint)samplesPerPixel, (uint)currentExtraOffset);
      for (var i = 0; i < samplesPerPixel; ++i)
        BinaryPrimitives.WriteUInt16LittleEndian(span[(currentExtraOffset + i * 2)..], (ushort)bitsPerSample);
      currentExtraOffset += bpsExternalSize;
    }

    _WriteIfdEntry(span, ref pos, _TAG_COMPRESSION, _TYPE_SHORT, 1, 1); // Uncompressed
    _WriteIfdEntry(span, ref pos, _TAG_PHOTOMETRIC_INTERPRETATION, _TYPE_SHORT, 1, photometric);
    _WriteIfdEntry(span, ref pos, _TAG_STRIP_OFFSETS, _TYPE_LONG, 1, (uint)pixelDataOffset);
    _WriteIfdEntry(span, ref pos, _TAG_SAMPLES_PER_PIXEL, _TYPE_SHORT, 1, (uint)samplesPerPixel);
    _WriteIfdEntry(span, ref pos, _TAG_ROWS_PER_STRIP, _TYPE_LONG, 1, (uint)height);
    _WriteIfdEntry(span, ref pos, _TAG_STRIP_BYTE_COUNTS, _TYPE_LONG, 1, (uint)totalPixelBytes);

    // DNGVersion (4 bytes of type BYTE, fits in value field)
    var dngVersion = file.DngVersion;
    var versionValue = (uint)(dngVersion.Length >= 4
      ? dngVersion[0] | (dngVersion[1] << 8) | (dngVersion[2] << 16) | (dngVersion[3] << 24)
      : dngVersion.Length >= 1 ? dngVersion[0] : 1);
    _WriteIfdEntry(span, ref pos, _TAG_DNG_VERSION, _TYPE_BYTE, 4, versionValue);

    // DNGBackwardVersion (4 bytes of type BYTE, fits in value field)
    var backwardVersion = (uint)(1 | (4 << 8)); // [1,4,0,0] stored as LE uint32
    _WriteIfdEntry(span, ref pos, _TAG_DNG_BACKWARD_VERSION, _TYPE_BYTE, 4, backwardVersion);

    // UniqueCameraModel (ASCII)
    if (cameraModelBytes.Length <= 4) {
      uint modelValue = 0;
      for (var i = 0; i < cameraModelBytes.Length; ++i)
        modelValue |= (uint)cameraModelBytes[i] << (i * 8);
      _WriteIfdEntry(span, ref pos, _TAG_UNIQUE_CAMERA_MODEL, _TYPE_ASCII, (uint)cameraModelBytes.Length, modelValue);
    } else {
      _WriteIfdEntry(span, ref pos, _TAG_UNIQUE_CAMERA_MODEL, _TYPE_ASCII, (uint)cameraModelBytes.Length, (uint)currentExtraOffset);
      cameraModelBytes.AsSpan(0, cameraModelBytes.Length).CopyTo(result.AsSpan(currentExtraOffset));
      currentExtraOffset += cameraModelBytes.Length;
    }

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
