using System;
using System.IO;
using FileFormat.JpegXr.JxrLib;

namespace FileFormat.JpegXr;

/// <summary>Reads JPEG XR (T.833 container + T.832 codestream) files.</summary>
/// <remarks>
/// The codestream is decoded by a managed conversion of Microsoft's jxrlib decoder (see
/// <c>JxrLib/THIRD-PARTY-NOTICE.JxrLib.txt</c>), into the file's own pixel format first and from
/// there into the nearest raster (<see cref="JpegXrRaster"/>).
/// </remarks>
public static class JpegXrReader {

  private const int _MIN_FILE_SIZE = 14;

  /// <summary>The little-endian word represented by the on-disk bytes BC 01.</summary>
  internal const ushort JPEGXR_MAGIC = 0x01BC;

  public static JpegXrFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("JPEG XR file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static JpegXrFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static JpegXrFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static JpegXrFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid JPEG XR file.");
    if (data[0] != (byte)'I' || data[1] != (byte)'I')
      throw new InvalidDataException($"Invalid JPEG XR byte order: expected 'II', got 0x{data[0]:X2} 0x{data[1]:X2}.");

    var header = JpegXrHeader.ReadFrom(data);
    if (header.Magic != JPEGXR_MAGIC)
      throw new InvalidDataException($"Invalid JPEG XR magic: expected 0x{JPEGXR_MAGIC:X4}, got 0x{header.Magic:X4}.");

    var ifdOffset = checked((int)header.IfdOffset);
    if (ifdOffset < 8 || ifdOffset > data.Length - 2)
      throw new InvalidDataException($"Invalid IFD offset: {ifdOffset}.");

    var decoded = JxrLibDecoder.Decode(data);
    var raster = JpegXrRaster.ToRawImage(decoded);
    return new() {
      Width = raster.Width,
      Height = raster.Height,
      ComponentCount = JpegXrRaster.ComponentCount(decoded.PixelFormatName),
      PixelData = raster.PixelData,
      Format = raster.Format,
      SourcePixelFormat = decoded.PixelFormatName,
      SourceSamples = decoded.Pixels,
      SourceStride = decoded.Stride,
    };
  }
}
