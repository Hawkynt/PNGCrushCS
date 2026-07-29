using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MagicPainter;

/// <summary>Reads Magic Painter (.mgp) files from bytes, streams, or file paths.</summary>
public static class MagicPainterReader {

  public static MagicPainterFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Magic Painter file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MagicPainterFile FromStream(Stream stream) {
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

  public static MagicPainterFile FromSpan(ReadOnlySpan<byte> data) {
    // Magic Painter has no signature — the fixed size is the only thing identifying it.
    if (data.Length != MagicPainterFile.FileSize)
      throw new InvalidDataException($"A Magic Painter file is exactly {MagicPainterFile.FileSize} bytes, got {data.Length}.");

    var registers = new byte[Atari8BitGraphics.ColorRegisterCount];
    data[..registers.Length].CopyTo(registers);

    // The stored bitmap is one byte short of a screen; the missing byte reads as zero.
    var bitmap = new byte[MagicPainterFile.BitmapDataSize];
    data.Slice(MagicPainterFile.BitmapOffset, MagicPainterFile.StoredBitmapSize).CopyTo(bitmap);

    return new() {
      BitmapData = bitmap,
      ColorRegisters = registers,
      Rainbow = data[MagicPainterFile.RainbowOffset],
    };
  }

  public static MagicPainterFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
