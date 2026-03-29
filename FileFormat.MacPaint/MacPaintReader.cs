using System;
using System.IO;

namespace FileFormat.MacPaint;

/// <summary>Reads MacPaint files from bytes, streams, or file paths.</summary>
public static class MacPaintReader {

  private const int _EXPECTED_PIXEL_DATA_SIZE = 72 * 720; // 51840 bytes

  public static MacPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MacPaint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MacPaintFile FromStream(Stream stream) {
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

  public static MacPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < MacPaintHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid MacPaint file.");

    var offset = _DetectMacBinaryOffset(data);
    if (data.Length - offset < MacPaintHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid MacPaint file.");

    var span = data.AsSpan(offset);
    var header = MacPaintHeader.ReadFrom(span);

    var compressedData = data[(offset + MacPaintHeader.StructSize)..];
    var pixelData = PackBitsCompressor.Decompress(compressedData, _EXPECTED_PIXEL_DATA_SIZE);

    return new MacPaintFile {
      Width = 576,
      Height = 720,
      Version = header.Version,
      BrushPatterns = header.Patterns,
      PixelData = pixelData
    };
  }

  private static int _DetectMacBinaryOffset(byte[] data) {
    if (data.Length <= 128 + MacPaintHeader.StructSize)
      return 0;

    // MacBinary header detection:
    // byte 0 must be 0 (old version field)
    // byte 74 must be 0 (zero fill)
    // byte 82 must be 0 (zero fill)
    // filename length (byte 1) must be 1..63
    if (data[0] == 0 && data[74] == 0 && data[82] == 0) {
      var nameLen = data[1];
      if (nameLen >= 1 && nameLen <= 63)
        return 128;
    }

    return 0;
  }
}
