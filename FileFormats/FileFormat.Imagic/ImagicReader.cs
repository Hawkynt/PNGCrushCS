using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Imagic;

/// <summary>Reads Imagic files from bytes, streams, or file paths.</summary>
public static class ImagicReader {

  public static ImagicFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Imagic file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ImagicFile FromStream(Stream stream) {
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

  public static ImagicFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= ImagicFile.DataOffset)
      throw new InvalidDataException($"An Imagic file is longer than its {ImagicFile.DataOffset}-byte header, got {data.Length}.");
    if (!data[..ImagicFile.Signature.Length].SequenceEqual(ImagicFile.Signature) || data[4] != 0)
      throw new InvalidDataException("Not an Imagic file: the IMDC tag is missing.");
    if (!data.Slice(ImagicFile.StampOffset, ImagicFile.Stamp.Length).SequenceEqual(ImagicFile.Stamp))
      throw new InvalidDataException("Not an Imagic file: the header stamp does not match.");

    var mode = data[ImagicFile.ModeOffset];
    if (mode > (byte)ImagicResolution.High)
      throw new InvalidDataException($"Imagic resolution {mode} is not one of the three the ST offers.");

    var palette = new short[ImagicFile.PaletteCount];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = BinaryPrimitives.ReadInt16BigEndian(data.Slice(ImagicFile.PaletteOffset + i * 2, 2));

    var reserved = new byte[ImagicFile.ReservedSize];
    data.Slice(ImagicFile.ReservedOffset, ImagicFile.ReservedSize).CopyTo(reserved);

    return new() {
      Resolution = (ImagicResolution)mode,
      Palette = palette,
      Reserved = reserved,
      ScreenData = ImagicCompressor.Decompress(data[ImagicFile.DataOffset..], data[ImagicFile.EscapeOffset]),
    };
  }

  public static ImagicFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
