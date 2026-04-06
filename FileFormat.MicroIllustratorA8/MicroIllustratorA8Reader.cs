using System;
using System.IO;

namespace FileFormat.MicroIllustratorA8;

/// <summary>Reads Micro Illustrator Atari 8-bit files from bytes, streams, or file paths.</summary>
public static class MicroIllustratorA8Reader {

  public static MicroIllustratorA8File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Micro Illustrator file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MicroIllustratorA8File FromStream(Stream stream) {
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

  public static MicroIllustratorA8File FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static MicroIllustratorA8File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != MicroIllustratorA8File.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Micro Illustrator data size: expected exactly {MicroIllustratorA8File.ExpectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[MicroIllustratorA8File.ExpectedFileSize];
    data.AsSpan(0, MicroIllustratorA8File.ExpectedFileSize).CopyTo(pixelData);

    return new MicroIllustratorA8File { PixelData = pixelData };
  }
}
