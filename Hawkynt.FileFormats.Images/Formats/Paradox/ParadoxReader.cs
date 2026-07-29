using System;
using System.IO;

namespace FileFormat.Paradox;

/// <summary>Reads Atari 8-bit Paradox (.mcpp) screens from bytes, streams, or file paths.</summary>
public static class ParadoxReader {

  public static ParadoxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Paradox screen not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ParadoxFile FromStream(Stream stream) {
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

  public static ParadoxFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != ParadoxFile.FileSize)
      throw new InvalidDataException($"A Paradox screen is exactly {ParadoxFile.FileSize} bytes, got {data.Length}.");

    var first = new byte[ParadoxFile.FieldDataSize];
    data[..ParadoxFile.FieldDataSize].CopyTo(first);

    var second = new byte[ParadoxFile.FieldDataSize];
    data.Slice(ParadoxFile.SecondFieldOffset, ParadoxFile.FieldDataSize).CopyTo(second);

    var firstColors = new byte[ParadoxFile.ColorsPerField];
    data.Slice(ParadoxFile.ColorsOffset, ParadoxFile.ColorsPerField).CopyTo(firstColors);

    var secondColors = new byte[ParadoxFile.ColorsPerField];
    data.Slice(ParadoxFile.ColorsOffset + ParadoxFile.ColorsPerField, ParadoxFile.ColorsPerField).CopyTo(secondColors);

    return new() {
      FirstField = first, SecondField = second,
      FirstFieldColors = firstColors, SecondFieldColors = secondColors,
    };
  }

  public static ParadoxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
