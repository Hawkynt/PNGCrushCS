using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Analyze;

/// <summary>Reads Analyze 7.5 files from bytes, streams, or file paths.</summary>
public static class AnalyzeReader {

  /// <summary>The fixed Analyze 7.5 header size in bytes.</summary>
  internal const int HEADER_SIZE = 348;

  public static AnalyzeFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Analyze header file not found.", file.FullName);

    var hdrBytes = File.ReadAllBytes(file.FullName);

    // Derive .img path by replacing extension
    var imgPath = Path.ChangeExtension(file.FullName, ".img");
    byte[] imgBytes;
    if (File.Exists(imgPath))
      imgBytes = File.ReadAllBytes(imgPath);
    else
      imgBytes = [];

    return _Parse(hdrBytes, imgBytes);
  }

  public static AnalyzeFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  /// <summary>Parses concatenated header+pixel data bytes (348-byte header followed by pixel data).</summary>
  public static AnalyzeFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static AnalyzeFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < HEADER_SIZE)
      throw new InvalidDataException($"Data too small for a valid Analyze 7.5 file (need at least {HEADER_SIZE} bytes, got {data.Length}).");

    var hdrBytes = data.AsSpan(0, HEADER_SIZE).ToArray();
    var imgBytes = data.Length > HEADER_SIZE
      ? data.AsSpan(HEADER_SIZE).ToArray()
      : [];

    return _Parse(hdrBytes, imgBytes);
  }

  private static AnalyzeFile _Parse(byte[] hdrBytes, byte[] imgBytes) {
    var span = hdrBytes.AsSpan();

    // offset 0: int32 sizeof_hdr must be 348
    var sizeofHdr = BinaryPrimitives.ReadInt32LittleEndian(span);
    if (sizeofHdr != HEADER_SIZE)
      throw new InvalidDataException($"Invalid Analyze sizeof_hdr: expected {HEADER_SIZE}, got {sizeofHdr}.");

    // offset 40: int16[8] dim array
    var width = BinaryPrimitives.ReadInt16LittleEndian(span[42..]);   // dim[1]
    var height = BinaryPrimitives.ReadInt16LittleEndian(span[44..]);  // dim[2]

    // offset 70: int16 datatype
    var datatype = BinaryPrimitives.ReadInt16LittleEndian(span[70..]);

    // offset 72: int16 bitpix
    var bitpix = BinaryPrimitives.ReadInt16LittleEndian(span[72..]);

    return new AnalyzeFile {
      Width = width,
      Height = height,
      DataType = (AnalyzeDataType)datatype,
      BitsPerPixel = bitpix,
      PixelData = imgBytes,
    };
  }
}
