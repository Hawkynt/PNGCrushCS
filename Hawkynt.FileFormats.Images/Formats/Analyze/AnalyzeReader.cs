using System;
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

    // Analyze is normally a pair, .hdr beside .img, but it is also written as one file with the
    // voxels straight after the header — which is what ToBytes here produces. Substituting an empty
    // array when the sibling is absent threw the second form's pixels away and returned a picture
    // that stated a size and carried nothing, so converting it indexed off the end of the buffer.
    var imgPath = Path.ChangeExtension(file.FullName, ".img");
    var imgBytes = File.Exists(imgPath)
      ? File.ReadAllBytes(imgPath)
      : hdrBytes.Length > HEADER_SIZE ? hdrBytes[HEADER_SIZE..] : [];

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
  public static AnalyzeFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < HEADER_SIZE)
      throw new InvalidDataException($"Data too small for a valid Analyze 7.5 file (need at least {HEADER_SIZE} bytes, got {data.Length}).");

    var hdrBytes = data.Slice(0, HEADER_SIZE).ToArray();
    var imgBytes = data.Length > HEADER_SIZE
      ? data[HEADER_SIZE..].ToArray()
      : [];

    return _Parse(hdrBytes, imgBytes);
    }

  public static AnalyzeFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static AnalyzeFile _Parse(byte[] hdrBytes, byte[] imgBytes) {
    var header = AnalyzeHeader.ReadFrom(hdrBytes);

    if (header.SizeofHdr != HEADER_SIZE)
      throw new InvalidDataException($"Invalid Analyze sizeof_hdr: expected {HEADER_SIZE}, got {header.SizeofHdr}.");

    // Say the voxels are missing rather than hand back a picture whose declared size overruns the
    // buffer behind it; that read looks successful right up to the first conversion.
    var needed = (long)header.Width * header.Height * Math.Max((int)header.BitPix, 1) / 8;
    if (needed > 0 && imgBytes.Length < needed)
      throw new InvalidDataException(
        $"An Analyze {header.Width}x{header.Height} at {header.BitPix} bits needs {needed} bytes of voxels; "
        + $"{imgBytes.Length} were found, so the .img beside this header is missing or truncated.");

    return new AnalyzeFile {
      Width = header.Width,
      Height = header.Height,
      DataType = (AnalyzeDataType)header.DataType,
      BitsPerPixel = header.BitPix,
      PixelData = imgBytes,
    };
  }
}
