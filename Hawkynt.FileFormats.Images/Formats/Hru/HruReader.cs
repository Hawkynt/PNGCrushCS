using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Gif;

namespace FileFormat.Hru;

/// <summary>Reads HRU pictures from bytes, streams, or file paths.</summary>
public static class HruReader {

  /// <summary>What the GIF signature would have been.</summary>
  private static ReadOnlySpan<byte> _GifSignature => [
    (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'7', (byte)'a',
  ];

  /// <summary>The byte that opens an image descriptor.</summary>
  private const byte _ImageSeparator = 0x2C;

  /// <summary>Set in the screen descriptor's flags when a global colour table follows.</summary>
  private const int _GlobalTableFlag = 0x80;

  public static HruFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("HRU picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HruFile FromStream(Stream stream) {
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

  public static HruFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < HruFile.MagicSize + HruFile.ScreenDescriptorSize + HruFile.ImageDescriptorSize
        || !data[..HruFile.MagicSize].SequenceEqual(HruFile.Magic))
      throw new InvalidDataException("Not an HRU picture: it does not open with the HRU signature.");

    var screen = data.Slice(HruFile.MagicSize, HruFile.ScreenDescriptorSize);
    var width = screen[0] | (screen[1] << 8);
    var height = screen[2] | (screen[3] << 8);
    var flags = screen[4];

    if (width < 1 || height < 1)
      throw new InvalidDataException($"An HRU picture states a size of {width} by {height}.");

    if ((flags & _GlobalTableFlag) == 0)
      throw new InvalidDataException("An HRU picture carries its colours in a global table and this one states none.");

    var paletteEntries = 1 << ((flags & 7) + 1);
    var paletteBytes = paletteEntries * 3;
    var paletteAt = HruFile.MagicSize + HruFile.ScreenDescriptorSize;
    var codedAt = paletteAt + paletteBytes + HruFile.ImageDescriptorSize;
    if (codedAt >= data.Length)
      throw new InvalidDataException($"An HRU picture of {paletteEntries} colours does not fit in {data.Length} bytes.");

    // Rebuilt as the GIF it is, with a signature and an image descriptor put back, and handed to the
    // GIF reader rather than decoded again here. The descriptor is written from the screen
    // descriptor's size because the ten bytes the file has there are not one and do not agree with
    // anything else in it.
    var rebuilt = new byte[_GifSignature.Length + HruFile.ScreenDescriptorSize + paletteBytes
                           + HruFile.ImageDescriptorSize + (data.Length - codedAt)];
    var at = 0;
    _GifSignature.CopyTo(rebuilt.AsSpan(at));
    at += _GifSignature.Length;
    screen.CopyTo(rebuilt.AsSpan(at));
    at += HruFile.ScreenDescriptorSize;
    data.Slice(paletteAt, paletteBytes).CopyTo(rebuilt.AsSpan(at));
    at += paletteBytes;

    rebuilt[at] = _ImageSeparator;
    rebuilt[at + 1] = 0;
    rebuilt[at + 2] = 0;
    rebuilt[at + 3] = 0;
    rebuilt[at + 4] = 0;
    rebuilt[at + 5] = (byte)width;
    rebuilt[at + 6] = (byte)(width >> 8);
    rebuilt[at + 7] = (byte)height;
    rebuilt[at + 8] = (byte)(height >> 8);
    rebuilt[at + 9] = 0;
    at += HruFile.ImageDescriptorSize;

    data[codedAt..].CopyTo(rebuilt.AsSpan(at));

    var image = GifFile.ToRawImage(GifReader.FromBytes(rebuilt));
    var indexed = PixelConverter.Convert(image, PixelFormat.Indexed8);

    // The size the screen descriptor gives is only worth trusting because the coded data fills it,
    // so a decode that came back some other size is a decode of something else.
    if (indexed.Width != width || indexed.Height != height)
      throw new InvalidDataException($"An HRU picture states {width} by {height} and its data makes {indexed.Width} by {indexed.Height}.");

    return new() {
      Width = indexed.Width,
      Height = indexed.Height,
      PixelData = indexed.PixelData,
      Palette = indexed.Palette ?? new byte[paletteBytes],
      PaletteCount = indexed.PaletteCount > 0 ? indexed.PaletteCount : paletteEntries,
    };
  }

  public static HruFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
