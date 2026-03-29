using System;
using System.IO;

namespace FileFormat.FaceServer;

/// <summary>Reads FaceServer image files from bytes, streams, or file paths.</summary>
public static class FaceServerReader {

  private const int _MIN_FILE_SIZE = FaceServerFile.PixelCount;

  public static FaceServerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FaceServer file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FaceServerFile FromStream(Stream stream) {
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

  public static FaceServerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException($"Data too small for a valid FaceServer file: expected at least {_MIN_FILE_SIZE} bytes, got {data.Length}.");

    var headerEnd = _FindPixelDataStart(data);
    var remaining = data.Length - headerEnd;

    if (remaining < FaceServerFile.PixelCount)
      throw new InvalidDataException($"Not enough pixel data after header: expected {FaceServerFile.PixelCount} bytes, got {remaining}.");

    var pixelData = new byte[FaceServerFile.PixelCount];
    data.AsSpan(headerEnd, FaceServerFile.PixelCount).CopyTo(pixelData.AsSpan(0));

    return new FaceServerFile {
      PixelData = pixelData,
    };
  }

  private static int _FindPixelDataStart(byte[] data) {
    var offset = 0;
    while (offset < data.Length) {
      var lineEnd = Array.IndexOf(data, (byte)'\n', offset);
      if (lineEnd < 0)
        return offset;

      var hasColon = false;
      for (var i = offset; i < lineEnd; ++i)
        if (data[i] == (byte)':') {
          hasColon = true;
          break;
        }

      if (!hasColon)
        return offset;

      offset = lineEnd + 1;
    }

    return offset;
  }
}
