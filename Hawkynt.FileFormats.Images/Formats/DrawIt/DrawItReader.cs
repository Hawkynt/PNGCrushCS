using System;
using System.IO;

namespace FileFormat.DrawIt;

/// <summary>Reads DrawIt (.dit) files from bytes, streams, or file paths.</summary>
public static class DrawItReader {

  public static DrawItFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("DrawIt file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DrawItFile FromStream(Stream stream) {
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

  public static DrawItFile FromSpan(ReadOnlySpan<byte> data) {
    // DrawIt has no signature — the fixed size is the only thing identifying it.
    if (data.Length != DrawItFile.FileSize)
      throw new InvalidDataException($"A DrawIt file is exactly {DrawItFile.FileSize} bytes, got {data.Length}.");

    var bitmap = new byte[DrawItFile.BitmapDataSize];
    data[..DrawItFile.BitmapDataSize].CopyTo(bitmap);

    var registers = new byte[FileFormat.Core.Atari8BitGraphics.ColorRegisterCount];
    data.Slice(DrawItFile.ColorRegisterOffset, registers.Length).CopyTo(registers);

    return new() {
      BitmapData = bitmap,
      ColorRegisters = registers,
    };
  }

  public static DrawItFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
