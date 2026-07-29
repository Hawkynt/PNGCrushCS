using System;
using System.IO;

namespace FileFormat.SeattleFilmWorks;

/// <summary>Reads Seattle Film Works (SFW/PWP) files from bytes, streams, or file paths.</summary>
public static class SeattleFilmWorksReader {

  public static SeattleFilmWorksFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SFW file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SeattleFilmWorksFile FromStream(Stream stream) {
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

  public static SeattleFilmWorksFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < SeattleFilmWorksFile.MIN_FILE_SIZE)
      throw new InvalidDataException($"Data too small for SFW format: expected at least {SeattleFilmWorksFile.MIN_FILE_SIZE} bytes, got {data.Length}.");

    var header = data.Slice(0, SeattleFilmWorksFile.MAGIC_LENGTH);
    if (!header.SequenceEqual(SeattleFilmWorksFile.SfwMagic) && !header.SequenceEqual(SeattleFilmWorksFile.PwpMagic))
      throw new InvalidDataException("Invalid SFW signature: expected 'SFW94A' or 'SFW95A'.");

    // Find the JPEG SOI marker (0xFF 0xD8) after the magic
    var jpegOffset = _FindJpegSoi(data, SeattleFilmWorksFile.MAGIC_LENGTH);
    if (jpegOffset < 0)
      throw new InvalidDataException("No JPEG SOI marker (0xFF 0xD8) found after SFW header.");

    var jpegLength = data.Length - jpegOffset;
    var jpegData = new byte[jpegLength];
    data.Slice(jpegOffset, jpegLength).CopyTo(jpegData);

    // Detect our round-trip payload: SOI immediately followed by APP15 (FFEF) with
    // a Width(2 LE) + Height(2 LE) + raw RGB bytes layout. Anything else is treated
    // as opaque JPEG data, exposed through JpegData for an external decoder.
    if (jpegData.Length >= 4 + 6 && jpegData[2] == 0xFF && jpegData[3] == 0xEF) {
      var segLen = (jpegData[4] << 8) | jpegData[5];
      if (segLen >= 6 && 4 + segLen <= jpegData.Length) {
        var w = jpegData[6] | (jpegData[7] << 8);
        var h = jpegData[8] | (jpegData[9] << 8);
        var pixelBytes = segLen - 6;
        var expected = w * h * 3;
        if (w > 0 && h > 0 && pixelBytes >= expected) {
          var pixels = new byte[expected];
          jpegData.AsSpan(10, expected).CopyTo(pixels);
          return new SeattleFilmWorksFile {
            Width = w,
            Height = h,
            JpegData = [],
            PixelData = pixels,
          };
        }
      }
    }

    // Since we do not decode JPEG internally, we store an empty pixel buffer.
    // The caller can use JpegData with an external JPEG decoder to get pixels.
    return new SeattleFilmWorksFile {
      Width = 0,
      Height = 0,
      JpegData = jpegData,
      PixelData = [],
    };

  }

  public static SeattleFilmWorksFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Searches for the JPEG SOI marker (0xFF 0xD8) starting at the given offset.</summary>
  private static int _FindJpegSoi(ReadOnlySpan<byte> data, int startOffset) {
    for (var i = startOffset; i < data.Length - 1; ++i)
      if (data[i] == 0xFF && data[i + 1] == 0xD8)
        return i;

    return -1;
  }
}
