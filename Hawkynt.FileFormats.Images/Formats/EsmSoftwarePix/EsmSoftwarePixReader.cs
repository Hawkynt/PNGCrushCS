using System;
using System.IO;
using FileFormat.Wrappers;

namespace FileFormat.EsmSoftwarePix;

/// <summary>Reads an Esm Software picture from bytes, streams, or file paths.</summary>
public static class EsmSoftwarePixReader {

  public static EsmSoftwarePixFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EsmSoftwarePixFile FromStream(Stream stream) {
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

  public static EsmSoftwarePixFile FromSpan(ReadOnlySpan<byte> data) {
    var (embedded, isPng) = WrappedPicture.Extract(data, EsmSoftwarePixFile.Magic, "an Esm Software picture");
    return new() { Embedded = embedded, IsPng = isPng };
  }

  public static EsmSoftwarePixFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
