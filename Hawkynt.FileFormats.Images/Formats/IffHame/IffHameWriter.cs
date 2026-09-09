using System;
using System.IO;
using FileFormat.Ilbm;

namespace FileFormat.IffHame;

/// <summary>Assembles IFF HAM-E bytes from an <see cref="IffHameFile"/>.</summary>
/// <remarks>
/// HAM-E has no private IFF form type. The device watches an ordinary four-plane Amiga HIRES ILBM,
/// joins each pair of carrier pixels into one eight-bit value, and recognises its mode/palette data
/// from the first one to four scanlines. This writer emits that carrier rather than inventing a
/// private chunk that period software and real hardware would not understand.
/// </remarks>
public static class IffHameWriter {

  private const uint _HIRES = 0x8000;
  private const uint _LACE = 0x0004;

  public static byte[] ToBytes(IffHameFile file) {
    IffHameFile.Validate(file, nameof(file));

    var paletteLines = file.PaletteCount / IffHameCodec.PaletteEntriesPerLine;
    var carrierHeight = file.Height + paletteLines;
    var commands = new byte[file.Width * carrierHeight];

    for (var line = 0; line < paletteLines; ++line) {
      var row = commands.AsSpan(line * file.Width, file.Width);
      IffHameCodec.MagicCookie.CopyTo(row);
      row[IffHameCodec.MagicCookie.Length] = IffHameCodec.HoldAndModifyMode;

      var sourceAt = line * IffHameCodec.PaletteEntriesPerLine * 3;
      file.Palette.AsSpan(sourceAt, IffHameCodec.PaletteEntriesPerLine * 3)
        .CopyTo(row[(IffHameCodec.MagicCookie.Length + 1)..]);
    }

    file.PixelData.CopyTo(commands, paletteLines * file.Width);

    var carrierWidth = file.Width * 2;
    var carrier = new IlbmFile {
      Width = carrierWidth,
      Height = carrierHeight,
      NumPlanes = 4,
      Compression = IlbmCompression.ByteRun1,
      Masking = IlbmMasking.None,
      TransparentColor = 0,
      XAspect = file.Interlaced ? (byte)10 : (byte)5,
      YAspect = 11,
      PageWidth = carrierWidth,
      PageHeight = carrierHeight,
      PixelData = IffHameCodec.CommandsToCarrier(commands),
      Palette = IffHameCodec.CreateCarrierPalette(),
      ViewportMode = _HIRES | (file.Interlaced ? _LACE : 0),
    };

    return IlbmWriter.ToBytes(carrier);
  }

  public static void ToStream(IffHameFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    var bytes = ToBytes(file);
    stream.Write(bytes, 0, bytes.Length);
  }

  public static void ToFile(IffHameFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
