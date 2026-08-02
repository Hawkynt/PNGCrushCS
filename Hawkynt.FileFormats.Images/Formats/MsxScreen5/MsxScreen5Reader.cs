using System;
using System.IO;

namespace FileFormat.MsxScreen5;

/// <summary>Reads MSX2 Screen 5 image files from bytes, streams, or file paths.</summary>
public static class MsxScreen5Reader {

  public static MsxScreen5File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MSX Screen 5 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MsxScreen5File FromStream(Stream stream) {
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

  public static MsxScreen5File FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < MsxScreen5File.PixelDataSize)
      throw new InvalidDataException($"Data too small for a valid MSX Screen 5 file: got {data.Length} bytes, need at least {MsxScreen5File.PixelDataSize}.");

    var hasBsave = data.Length >= MsxScreen5File.BsaveHeaderSize + MsxScreen5File.PixelDataSize && data[0] == MsxScreen5File.BsaveMagic;
    var rawOffset = hasBsave ? MsxScreen5File.BsaveHeaderSize : 0;
    var rawLength = data.Length - rawOffset;

    if (rawLength < MsxScreen5File.PixelDataSize)
      throw new InvalidDataException($"Data too small for MSX Screen 5 pixel data after header: got {rawLength} bytes, need {MsxScreen5File.PixelDataSize}.");

    var span = data[rawOffset..];

    var pixelData = new byte[MsxScreen5File.PixelDataSize];
    span[..MsxScreen5File.PixelDataSize].CopyTo(pixelData);

    // The palette is the last thing in the file, not the thirty-two bytes that follow the pixels.
    // Screen 5 shows 212 lines of a page that holds more, so a saved page runs past what is drawn
    // and the palette sits after all of it — reading at the end of the drawn part picks up picture
    // data and paints the whole thing in it.
    byte[]? palette = null;
    if (rawLength >= MsxScreen5File.FullDataSize) {
      palette = new byte[MsxScreen5File.PaletteSize];
      span[^MsxScreen5File.PaletteSize..].CopyTo(palette);
    }

    return new() {
      PixelData = pixelData,
      Palette = palette,
      HasBsaveHeader = hasBsave,
    };
    }

  /// <summary>
  /// Reads a picture from a byte array.
  /// </summary>
  /// <remarks>
  /// A call into the by-span reader rather than a second copy of it. The two used to be separate and
  /// that is how a correction half-lands: the Prism Paint reader had the same pair, its size fix
  /// went into one of them, and the other went on reading the signature as the picture's size while
  /// the corpus reported the file perfect.
  /// </remarks>
  public static MsxScreen5File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
