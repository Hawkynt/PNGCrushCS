using System;
using System.IO;

namespace FileFormat.AtariGraphics10;

/// <summary>Reads Atari Graphics 10 (GTIA 9-color) images from bytes, streams, or file paths.</summary>
/// <remarks>
/// A picture is 7680 bytes of screen, and most files carry nine more: the colour registers the
/// screen was drawn with. Those were refused for being nine bytes too long, so the only files this
/// format has were exactly the ones it would not open — and the picture was then painted in a
/// hard-coded palette that has nothing to do with what was saved.
/// <para/>
/// The by-bytes entry kept its own copy of the check, which is how a correction lands in half a
/// reader; it forwards now.
/// </remarks>
public static class AtariGraphics10Reader {

  public static AtariGraphics10File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari Graphics 10 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariGraphics10File FromStream(Stream stream) {
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

  public static AtariGraphics10File FromSpan(ReadOnlySpan<byte> data) {
    var withRegisters = AtariGraphics10File.FileSize + AtariGraphics10File.PaletteColors;
    if (data.Length != AtariGraphics10File.FileSize && data.Length != withRegisters)
      throw new InvalidDataException(
        $"An Atari Graphics 10 picture is {AtariGraphics10File.FileSize} bytes, or {withRegisters} with its colour registers; this file is {data.Length}.");

    var pixelData = data[..AtariGraphics10File.FileSize].ToArray();
    var registers = data.Length == withRegisters
      ? data.Slice(AtariGraphics10File.FileSize, AtariGraphics10File.PaletteColors).ToArray()
      : null;

    return new AtariGraphics10File { PixelData = pixelData, Registers = registers };
  }

  public static AtariGraphics10File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
