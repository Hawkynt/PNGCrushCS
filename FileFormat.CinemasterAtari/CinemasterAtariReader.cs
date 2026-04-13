using System;
using System.IO;

namespace FileFormat.CinemasterAtari;

/// <summary>Reads Cinemaster Atari animation files from bytes, streams, or file paths.</summary>
public static class CinemasterAtariReader {

  public static CinemasterAtariFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Cinemaster Atari file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CinemasterAtariFile FromStream(Stream stream) {
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

  public static CinemasterAtariFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < CinemasterAtariFile.HeaderSize)
      throw new InvalidDataException($"Cinemaster Atari data too small: minimum {CinemasterAtariFile.HeaderSize} bytes required, got {data.Length}.");

    var frameCount = (ushort)(data[0] | (data[1] << 8));
    var expectedSize = CinemasterAtariFile.HeaderSize + frameCount * CinemasterAtariFile.FrameSize;

    if (data.Length < expectedSize)
      throw new InvalidDataException($"Cinemaster Atari data too small for {frameCount} frames: expected {expectedSize} bytes, got {data.Length}.");

    var frames = new byte[frameCount][];
    for (var i = 0; i < frameCount; ++i) {
      frames[i] = new byte[CinemasterAtariFile.FrameSize];
      data.Slice(CinemasterAtariFile.HeaderSize + i * CinemasterAtariFile.FrameSize, CinemasterAtariFile.FrameSize).CopyTo(frames[i].AsSpan(0));
    }

    return new CinemasterAtariFile {
      FrameCount = frameCount,
      Frames = frames,
    };
    }

  public static CinemasterAtariFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < CinemasterAtariFile.HeaderSize)
      throw new InvalidDataException($"Cinemaster Atari data too small: minimum {CinemasterAtariFile.HeaderSize} bytes required, got {data.Length}.");

    var frameCount = (ushort)(data[0] | (data[1] << 8));
    var expectedSize = CinemasterAtariFile.HeaderSize + frameCount * CinemasterAtariFile.FrameSize;

    if (data.Length < expectedSize)
      throw new InvalidDataException($"Cinemaster Atari data too small for {frameCount} frames: expected {expectedSize} bytes, got {data.Length}.");

    var frames = new byte[frameCount][];
    for (var i = 0; i < frameCount; ++i) {
      frames[i] = new byte[CinemasterAtariFile.FrameSize];
      data.AsSpan(CinemasterAtariFile.HeaderSize + i * CinemasterAtariFile.FrameSize, CinemasterAtariFile.FrameSize).CopyTo(frames[i].AsSpan(0));
    }

    return new CinemasterAtariFile {
      FrameCount = frameCount,
      Frames = frames,
    };
  }
}
