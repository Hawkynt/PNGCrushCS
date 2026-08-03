using System;
using System.IO;
using FileFormat.Wrappers;

namespace FileFormat.PhotoLine;

/// <summary>Reads a Photo Line document from bytes, streams, or file paths.</summary>
public static class PhotoLineReader {

  public static PhotoLineFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PhotoLineFile FromStream(Stream stream) {
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

  public static PhotoLineFile FromSpan(ReadOnlySpan<byte> data) {
    var (embedded, isPng) = WrappedPicture.Extract(data, PhotoLineFile.Magic, "a Photo Line document");
    return new() { Embedded = embedded, IsPng = isPng };
  }

  public static PhotoLineFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
