using System;
using System.IO;

namespace FileFormat.Interlace8;

/// <summary>Reads Atari Interlace Mode files from bytes, streams, or file paths.</summary>
public static class Interlace8Reader {

  public static Interlace8File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Interlace Mode file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Interlace8File FromStream(Stream stream) {
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

  public static Interlace8File FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static Interlace8File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != Interlace8File.ExpectedFileSize)
      throw new InvalidDataException($"Interlace Mode file must be exactly {Interlace8File.ExpectedFileSize} bytes, got {data.Length}.");

    var frame1 = new byte[Interlace8File.FrameSize];
    var frame2 = new byte[Interlace8File.FrameSize];
    data.AsSpan(0, Interlace8File.FrameSize).CopyTo(frame1.AsSpan(0));
    data.AsSpan(Interlace8File.FrameSize, Interlace8File.FrameSize).CopyTo(frame2.AsSpan(0));

    return new Interlace8File {
      Frame1Data = frame1,
      Frame2Data = frame2,
    };
  }
}
