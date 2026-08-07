using System;
using System.IO;

namespace FileFormat.CutCreator;

/// <summary>Reads Cut Creator pictures from bytes, streams, or file paths.</summary>
public static class CutCreatorReader {

  public static CutCreatorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Cut Creator picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CutCreatorFile FromStream(Stream stream) {
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

  public static CutCreatorFile FromSpan(ReadOnlySpan<byte> data) {
    // Nothing in the file says what it is, so its length has to. Accepting anything longer would
    // take every 1188-byte file of any format at all, and Dr. Halo shares this extension.
    if (data.Length != CutCreatorFile.FileSize)
      throw new InvalidDataException(
        $"A Cut Creator picture is exactly {CutCreatorFile.FileSize} bytes, got {data.Length}.");

    return new() { PixelData = data.ToArray() };
  }

  public static CutCreatorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
