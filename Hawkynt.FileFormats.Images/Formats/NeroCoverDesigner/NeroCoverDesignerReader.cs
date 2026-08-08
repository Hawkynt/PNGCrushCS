using System;
using System.IO;
using FileFormat.Wrappers;

namespace FileFormat.NeroCoverDesigner;

/// <summary>Reads a Nero cover from bytes, streams, or file paths.</summary>
public static class NeroCoverDesignerReader {

  public static NeroCoverDesignerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NeroCoverDesignerFile FromStream(Stream stream) {
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

  public static NeroCoverDesignerFile FromSpan(ReadOnlySpan<byte> data) {
    var (embedded, isPng) = WrappedPicture.ExtractLargest(data, NeroCoverDesignerFile.Magic, "a Nero cover");
    return new() { Embedded = embedded, IsPng = isPng };
  }

  public static NeroCoverDesignerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
