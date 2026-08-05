using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.LogoPainter;

/// <summary>Reads Logo Painter 3 pictures from bytes, streams, or file paths.</summary>
public static class LogoPainterReader {

  public static LogoPainterFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Logo Painter file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static LogoPainterFile FromStream(Stream stream) {
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

  public static LogoPainterFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < LogoPainterFile.ExpectedFileSize)
      throw new InvalidDataException(
        $"A Logo Painter 3 picture is {LogoPainterFile.ExpectedFileSize} bytes, or a little more where it carries its own display routine; this file is {data.Length}.");

    return new() {
      LoadAddress = BinaryPrimitives.ReadUInt16LittleEndian(data),
      Screen = data.Slice(LogoPainterFile.ScreenOffset, LogoPainterFile.Columns * LogoPainterFile.Rows).ToArray(),
      CharacterSet = data.Slice(LogoPainterFile.CharacterSetOffset, LogoPainterFile.CharacterSetSize).ToArray(),
      Colors = _ColorsFrom(data),
    };
  }

  /// <summary>
  /// What the four patterns show: what the file names, or the stock four where it names nothing.
  /// </summary>
  /// <remarks>
  /// Pattern 11 takes its colour from colour memory, whose low three bits are the colour and whose
  /// fourth bit is the flag that puts the cell in multicolour at all — so it is masked to three bits
  /// and not four, which is why a colour memory of 0xFF is seven rather than fifteen.
  /// </remarks>
  private static byte[] _ColorsFrom(ReadOnlySpan<byte> data) {
    var background = data[LogoPainterFile.BackgroundRegisterOffset];
    var colorMemory = data[LogoPainterFile.ColorMemoryOffset];
    var first = data[LogoPainterFile.MulticolorRegister1Offset];
    var second = data[LogoPainterFile.MulticolorRegister2Offset];

    // A picture saved without a display routine leaves the whole of the screen's unused tail at
    // 0xFF. That is not four colours, it is none, and every viewer draws its own instead.
    if (background == 0xFF && colorMemory == 0xFF && first == 0xFF && second == 0xFF)
      return LogoPainterFile.DefaultColors.ToArray();

    return [
      (byte)(background & 0x0F),
      (byte)(first & 0x0F),
      (byte)(second & 0x0F),
      (byte)(colorMemory & 0x07),
    ];
  }

  public static LogoPainterFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
