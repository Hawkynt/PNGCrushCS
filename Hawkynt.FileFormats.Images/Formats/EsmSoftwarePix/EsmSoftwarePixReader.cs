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

  public static EsmSoftwarePixFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static EsmSoftwarePixFile FromSpan(ReadOnlySpan<byte> data) {
    var (embedded, isPng) = WrappedPicture.Extract(data, EsmSoftwarePixFile.Magic, "an Esm Software picture");
    return new() { Embedded = embedded, IsPng = isPng };
  }

  public static EsmSoftwarePixFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
