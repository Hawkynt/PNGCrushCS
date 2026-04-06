using System;
using System.IO;

namespace FileFormat.Envi;

/// <summary>Reads ENVI files from bytes, streams, or file paths.</summary>
public static class EnviReader {

  private const int _MIN_SIZE = 5;

  public static EnviFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ENVI file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EnviFile FromStream(Stream stream) {
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

  public static EnviFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static EnviFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_SIZE)
      throw new InvalidDataException($"Data too small for a valid ENVI file: expected at least {_MIN_SIZE} bytes, got {data.Length}.");

    // validate magic: "ENVI" (45 4E 56 49) followed by CR (0x0D) or LF (0x0A)
    if (data[0] != 0x45 || data[1] != 0x4E || data[2] != 0x56 || data[3] != 0x49)
      throw new InvalidDataException("Invalid ENVI signature: expected 'ENVI' at offset 0.");

    if (data[4] != 0x0D && data[4] != 0x0A)
      throw new InvalidDataException("Invalid ENVI header: expected CR or LF after 'ENVI' magic.");

    var (fields, headerEndOffset) = EnviHeaderParser.Parse(data);

    var width = EnviHeaderParser.GetInt(fields, "samples");
    var height = EnviHeaderParser.GetInt(fields, "lines");
    var bands = EnviHeaderParser.GetInt(fields, "bands", 1);
    var dataType = EnviHeaderParser.GetInt(fields, "data type", 1);
    var byteOrder = EnviHeaderParser.GetInt(fields, "byte order");
    var headerOffset = EnviHeaderParser.GetInt(fields, "header offset");
    var interleave = EnviHeaderParser.ParseInterleave(EnviHeaderParser.GetString(fields, "interleave", "bsq"));

    var bytesPerSample = _BytesPerSample(dataType);
    var expectedPixelBytes = width * height * bands * bytesPerSample;

    // pixel data starts after the header text + any explicit header offset
    var pixelStart = headerEndOffset + headerOffset;
    var available = Math.Max(0, data.Length - pixelStart);
    var copyLen = Math.Min(expectedPixelBytes, available);

    var pixelData = new byte[expectedPixelBytes];
    if (copyLen > 0)
      data.AsSpan(pixelStart, copyLen).CopyTo(pixelData.AsSpan(0));

    return new EnviFile {
      Width = width,
      Height = height,
      Bands = bands,
      DataType = dataType,
      Interleave = interleave,
      ByteOrder = byteOrder,
      PixelData = pixelData
    };
  }

  private static int _BytesPerSample(int dataType) => dataType switch {
    1 => 1,   // uint8
    2 => 2,   // int16
    4 => 4,   // float32
    12 => 2,  // uint16
    _ => 1
  };
}
