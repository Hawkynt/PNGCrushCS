using System;
using System.IO;

namespace FileFormat.AtariAnimation;

/// <summary>Reads Atari Animation multi-frame files from bytes, streams, or file paths.</summary>
public static class AtariAnimationReader {

  public static AtariAnimationFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari Animation file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariAnimationFile FromStream(Stream stream) {
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

  public static AtariAnimationFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AtariAnimationFile.FrameSize)
      throw new InvalidDataException($"Atari Animation data too small: minimum {AtariAnimationFile.FrameSize} bytes required, got {data.Length}.");

    if (data.Length % AtariAnimationFile.FrameSize != 0)
      throw new InvalidDataException($"Atari Animation data size must be a multiple of {AtariAnimationFile.FrameSize} bytes, got {data.Length}.");

    var frameCount = data.Length / AtariAnimationFile.FrameSize;
    var frames = new byte[frameCount][];

    for (var i = 0; i < frameCount; ++i) {
      frames[i] = new byte[AtariAnimationFile.FrameSize];
      data.AsSpan(i * AtariAnimationFile.FrameSize, AtariAnimationFile.FrameSize).CopyTo(frames[i].AsSpan(0));
    }

    return new AtariAnimationFile { Frames = frames };
  }
}
