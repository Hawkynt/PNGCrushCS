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

  public static InterlaceStudioFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static InterlaceStudioFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < InterlaceStudioFile.FileSize)
      throw new InvalidDataException(
        $"An Interlace Studio picture takes {InterlaceStudioFile.FileSize} bytes; this file is {data.Length}.");

    return new() {
      Header = data[..InterlaceStudioFile.HeaderSize].ToArray(),
      FirstFrame = data.Slice(InterlaceStudioFile.FirstFrameOffset, InterlaceStudioFile.FrameSize).ToArray(),
      SecondFrame = data.Slice(InterlaceStudioFile.SecondFrameOffset, InterlaceStudioFile.FrameSize).ToArray(),
      Registers = data.Slice(
        InterlaceStudioFile.RegistersOffset,
        InterlaceStudioFile.RegisterTableCount * InterlaceStudioFile.RegisterTableSize).ToArray(),
    };
  }

  public static InterlaceStudioFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
