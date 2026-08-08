using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Gif;

namespace FileFormat.Hru;

/// <summary>Writes an HRU picture: the fixed signature, then the file as a GIF from the screen descriptor on.</summary>
/// <remarks>
/// The format is a GIF with its signature replaced, so the picture is encoded as a GIF by the writer
/// that already exists and the six-byte signature is swapped for the twenty-eight fixed bytes. Doing
/// the coding again here would be a second LZW encoder to keep in step with the first for no gain.
/// <para/>
/// The ten bytes where a GIF keeps its image descriptor are the one thing that is not GIF: real
/// files put something there that is not a descriptor and does not agree with the screen descriptor,
/// and the reader ignores them for that reason. A proper descriptor is written into that slot
/// anyway. It is what the coded data actually describes, so anything that does read those bytes gets
/// the truth rather than a copy of numbers observed in one file, and the reader here is unaffected
/// either way.
/// </remarks>
public static class HruWriter {

  /// <summary>What a GIF opens with, and so how much of it is dropped.</summary>
  private const int _GifSignatureSize = 6;

  /// <summary>Set in the screen descriptor's flags when a global colour table follows.</summary>
  private const int _GlobalTableFlag = 0x80;

  /// <summary>The byte that opens a block this file keeps none of.</summary>
  private const byte _ExtensionIntroducer = 0x21;

  public static byte[] ToBytes(HruFile file) {
    var image = new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData ?? new byte[file.Width * file.Height],
      Palette = file.Palette,
      PaletteCount = file.PaletteCount,
    };

    var gif = GifWriter.ToBytes(GifFile.FromRawImage(image));

    var flags = gif[_GifSignatureSize + 4];
    if ((flags & _GlobalTableFlag) == 0)
      throw new InvalidOperationException("An HRU picture carries its colours in a global table and the GIF written for it has none.");

    var paletteBytes = 3 * (1 << ((flags & 7) + 1));
    var at = _GifSignatureSize + HruFile.ScreenDescriptorSize + paletteBytes;

    // Nothing in this file may sit between the colour table and the coded data, so any extension the
    // GIF writer put there is stepped over rather than copied.
    while (at < gif.Length && gif[at] == _ExtensionIntroducer) {
      at += 2;
      while (gif[at] != 0)
        at += gif[at] + 1;

      ++at;
    }

    using var result = new MemoryStream();
    result.Write(HruFile.Magic);
    result.Write(gif.AsSpan(_GifSignatureSize, HruFile.ScreenDescriptorSize));
    result.Write(gif.AsSpan(_GifSignatureSize + HruFile.ScreenDescriptorSize, paletteBytes));
    result.Write(gif.AsSpan(at, HruFile.ImageDescriptorSize));
    result.Write(gif.AsSpan(at + HruFile.ImageDescriptorSize));

    return result.ToArray();
  }
}
