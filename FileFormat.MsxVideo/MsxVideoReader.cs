using System;
using System.IO;

namespace FileFormat.MsxVideo;

/// <summary>Reads Video MSX screen capture files from bytes, streams, or file paths.</summary>
public static class MsxVideoReader {

  public static MsxVideoFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Video MSX file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MsxVideoFile FromStream(Stream stream) {
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

  public static MsxVideoFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != MsxVideoFile.ExpectedFileSize)
      throw new InvalidDataException($"Video MSX file must be exactly {MsxVideoFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixels = new byte[MsxVideoFile.ExpectedFileSize];
    data.AsSpan(0, MsxVideoFile.ExpectedFileSize).CopyTo(pixels);

    return new() { PixelData = pixels };
  }
}
