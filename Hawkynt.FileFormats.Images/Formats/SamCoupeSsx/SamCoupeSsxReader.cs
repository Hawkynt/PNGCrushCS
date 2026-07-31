using System;
using System.IO;

namespace FileFormat.SamCoupeSsx;

/// <summary>Reads SAM Coupe screen dumps from bytes, streams, or file paths.</summary>
public static class SamCoupeSsxReader {

  public static SamCoupeSsxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Screen not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SamCoupeSsxFile FromStream(Stream stream) {
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

  public static SamCoupeSsxFile FromSpan(ReadOnlySpan<byte> data) {
    // Nothing marks the mode, and no two of them are the same size, so the length is the whole of
    // what there is to go on.
    switch (data.Length) {
      case SamCoupeSsxFile.Mode1Size:
      case SamCoupeSsxFile.Mode2Size:
      case SamCoupeSsxFile.Mode3Size:
      case SamCoupeSsxFile.Mode4Size:
        break;
      case SamCoupeSsxFile.ChunkySize:
        // A colour byte has seven bits; anything in the eighth means this is not a rendered dump.
        foreach (var b in data)
          if (b >= 128)
            throw new InvalidDataException("Not a SAM Coupe screen: a pixel names no colour the hardware has.");

        break;
      default:
        throw new InvalidDataException($"Not a SAM Coupe screen: {data.Length} bytes.");
    }

    return new() { Data = data.ToArray() };
  }

  public static SamCoupeSsxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
