using System;
using System.IO;
using FileFormat.Wrappers;

namespace FileFormat.PhotoStudio;

/// <summary>Reads a Photo Studio picture from bytes, streams, or file paths.</summary>
public static class PhotoStudioReader {

  public static PhotoStudioFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PhotoStudioFile FromStream(Stream stream) {
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

  public static PhotoStudioFile FromSpan(ReadOnlySpan<byte> data) {
    var (embedded, isPng) = WrappedPicture.Extract(data, PhotoStudioFile.Magic, "a Photo Studio picture");
    return new() { Embedded = embedded, IsPng = isPng };
  }

  public static PhotoStudioFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
