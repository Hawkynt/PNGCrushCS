using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.EzArt;

/// <summary>Reads EZ-Art Professional (.eza) files from bytes, streams, or file paths.</summary>
public static class EzArtReader {

  private const int _PALETTE_SIZE = 32;
  private const int _PIXEL_DATA_SIZE = 32000;
  private const int _PALETTE_ENTRIES = 16;

  public static EzArtFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("EZ-Art file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EzArtFile FromStream(Stream stream) {
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

  public static EzArtFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < EzArtFile.HeaderSize
        || !data[..EzArtFile.Signature.Length].SequenceEqual(EzArtFile.Signature))
      throw new InvalidDataException("Not an EZ-Art picture.");

    var palette = new short[EzArtFile.PaletteColors];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = (short)((data[EzArtFile.PaletteOffset + i * 2] << 8)
        | data[EzArtFile.PaletteOffset + i * 2 + 1]);

    // The screen is streamed a plane-row at a time and packed as one run; it has to be unpacked and
    // then put back into the interleaved order the display reads.
    var planeRows = PackBits.Unpack(data[EzArtFile.HeaderSize..], EzArtFile.ScreenSize);

    return new() {
      Width = EzArtFile.ScreenWidth,
      Height = EzArtFile.ScreenHeight,
      Palette = palette,
      PixelData = AtariStGraphics.FromPlaneRows(
        planeRows, EzArtFile.ScreenWidth, EzArtFile.ScreenHeight, EzArtFile.Planes),
    };
  }

  public static EzArtFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
