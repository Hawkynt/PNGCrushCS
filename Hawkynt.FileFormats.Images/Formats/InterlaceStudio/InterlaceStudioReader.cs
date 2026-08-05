using System;
using System.IO;

namespace FileFormat.InterlaceStudio;

/// <summary>Reads Interlace Studio pictures from bytes, streams, or file paths.</summary>
public static class InterlaceStudioReader {

  public static InterlaceStudioFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Interlace Studio file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static InterlaceStudioFile FromStream(Stream stream) {
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

  public static InterlaceStudioFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < InterlaceStudioFile.MinimumFileSize)
      throw new InvalidDataException(
        $"An Interlace Studio picture takes at least {InterlaceStudioFile.MinimumFileSize} bytes; this file is {data.Length}.");

    return new() {
      Header = data[..InterlaceStudioFile.HeaderSize].ToArray(),
      FirstFrame = data.Slice(InterlaceStudioFile.FirstFrameOffset, InterlaceStudioFile.FrameSize).ToArray(),
      SecondFrame = data.Slice(InterlaceStudioFile.SecondFrameOffset, InterlaceStudioFile.FrameSize).ToArray(),
    };
  }

  public static InterlaceStudioFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
