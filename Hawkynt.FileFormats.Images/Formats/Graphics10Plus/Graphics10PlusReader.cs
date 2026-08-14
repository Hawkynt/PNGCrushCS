using System;
using System.IO;

namespace FileFormat.Graphics10Plus;

/// <summary>Reads Atari 8-bit Graphics 10+ (.gr10p) screens from bytes, streams or file paths.</summary>
public static class Graphics10PlusReader {

  public static Graphics10PlusFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Graphics 10+ screen not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Graphics10PlusFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static Graphics10PlusFile FromSpan(ReadOnlySpan<byte> data) {
    // Nothing in the file says what it is: no signature, no header, no stated size. The one length
    // it has is the whole of the identification, which is why the check is exact rather than a
    // minimum — a longer file is some other format that happens to start the same way.
    if (data.Length != Graphics10PlusFile.FileSize)
      throw new InvalidDataException(
        $"A Graphics 10+ screen is exactly {Graphics10PlusFile.FileSize} bytes, got {data.Length}.");

    var screen = new byte[Graphics10PlusFile.ScreenDataSize];
    data[..Graphics10PlusFile.ScreenDataSize].CopyTo(screen);

    var registers = new byte[Graphics10PlusFile.RegisterCount];
    data.Slice(Graphics10PlusFile.RegisterOffset, Graphics10PlusFile.RegisterCount).CopyTo(registers);

    return new() { ScreenData = screen, Registers = registers };
  }

  public static Graphics10PlusFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
