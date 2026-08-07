using System;
using System.IO;

namespace FileFormat.CommodorePet;

/// <summary>Parses commodore pet petscii screen dump from raw bytes.</summary>
public static class CommodorePetReader {

  public static CommodorePetFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CommodorePetFile FromStream(Stream stream) {
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

  public static CommodorePetFile FromSpan(ReadOnlySpan<byte> data) {

    // A saved screen opens with the address it loads to, then the cells, then one colour each.
    var at = data.Length >= CommodorePetFile.LoadAddressSize + CommodorePetFile.CellCount * 2
      ? CommodorePetFile.LoadAddressSize
      : 0;

    if (data.Length < at + CommodorePetFile.CellCount)
      throw new InvalidDataException($"Data too small: {data.Length} bytes, expected at least {CommodorePetFile.CellCount}.");

    var codes = new byte[CommodorePetFile.CellCount];
    data.Slice(at, codes.Length).CopyTo(codes.AsSpan(0));

    // The colours are the last thing in the file and run to its end, which is what places them:
    // some of these carry a few bytes between the two areas, so counting forward from the screen
    // lands short.
    var colors = new byte[CommodorePetFile.CellCount];
    var colorsAt = data.Length - CommodorePetFile.CellCount;
    if (colorsAt >= at + CommodorePetFile.CellCount)
      data.Slice(colorsAt, colors.Length).CopyTo(colors.AsSpan(0));
    else
      colors.AsSpan().Fill(1); // nothing said, so the machine's own default

    // Whatever lies between the two areas is the caption. Keeping it means a screen read and
    // written back comes out as it went in, rather than losing the line the author put there.
    var captionAt = at + CommodorePetFile.CellCount;
    var caption = new byte[CommodorePetFile.CaptionSize];
    if (colorsAt - captionAt is var span && span > 0)
      data.Slice(captionAt, Math.Min(span, caption.Length)).CopyTo(caption.AsSpan(0));

    return new CommodorePetFile { PixelData = codes, CellColors = colors, Caption = caption };
  }

  public static CommodorePetFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
