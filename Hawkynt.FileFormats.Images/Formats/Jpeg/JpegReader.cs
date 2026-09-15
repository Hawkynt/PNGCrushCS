using System;
using System.IO;

namespace FileFormat.Jpeg;

/// <summary>Reads JPEG files from bytes, streams, or file paths.</summary>
public static class JpegReader {

  public static JpegFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("JPEG file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static JpegFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static JpegFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static JpegFile FromSpan(ReadOnlySpan<byte> data) => JpegManagedDecoder.Decode(data);
}
