using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace FileFormat.Miff;

/// <summary>Assembles MIFF file bytes from pixel data.</summary>
public static class MiffWriter {

  public static byte[] ToBytes(MiffFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _Assemble(file);
  }

  private static byte[] _Assemble(MiffFile file) {
    using var ms = new MemoryStream();

    // Write header
    var header = MiffHeaderParser.Format(file);
    ms.Write(header);

    // Write palette for PseudoClass
    if (file.ColorClass == MiffColorClass.PseudoClass && file.Palette != null)
      ms.Write(file.Palette);

    // Compress and write pixel data
    var bytesPerChannel = file.Depth / 8;
    var channelsPerPixel = _GetChannelsPerPixel(file.Type, file.Colorspace);
    var bytesPerPixel = channelsPerPixel * bytesPerChannel;

    switch (file.Compression) {
      case MiffCompression.Rle:
        var rleData = MiffRleCompressor.Compress(file.PixelData, bytesPerPixel);
        ms.Write(rleData);
        break;
      case MiffCompression.Zip:
        // The row is measured rather than counted up from the channels. Counting them from `type`
        // gets a palette picture wrong — one assembled from an indexed image states type=TrueColor
        // beside class=PseudoClass, so the row comes out three times its width and the file is cut
        // into a third as many chunks as it has rows. ImageMagick reads that anyway, because it only
        // takes the next chunk when its inflater has run out rather than once per row, so this is
        // not what was broken; the payload divided by the rows is simply the row, with nothing to be
        // wrong about.
        var zipData = _CompressZip(file.PixelData, file.Height > 0 ? file.PixelData.Length / file.Height : file.PixelData.Length);
        ms.Write(zipData);
        break;
      default:
        ms.Write(file.PixelData);
        break;
    }

    return ms.ToArray();
  }

  private static int _GetChannelsPerPixel(string type, string colorspace) {
    if (colorspace.Equals("CMYK", StringComparison.OrdinalIgnoreCase))
      return type.Contains("Alpha", StringComparison.OrdinalIgnoreCase) ? 5 : 4;

    if (type.Contains("Alpha", StringComparison.OrdinalIgnoreCase))
      return type.StartsWith("Grayscale", StringComparison.OrdinalIgnoreCase) ? 2 :
             type.StartsWith("Palette", StringComparison.OrdinalIgnoreCase) ? 2 : 4;

    if (type.StartsWith("Grayscale", StringComparison.OrdinalIgnoreCase))
      return 1;

    if (type.StartsWith("Palette", StringComparison.OrdinalIgnoreCase))
      return 1;

    return 3;
  }

  /// <summary>Writes the payload as one zlib stream cut into a length-prefixed chunk per row.</summary>
  /// <remarks>
  /// It was a raw deflate stream in one piece, which ImageMagick cannot read at all: its reader
  /// takes a four-byte big-endian length before each row and inflates that chunk on its own. Handing
  /// it the three simpler shapes settles that nothing else will do — a plain zlib stream is refused
  /// whether or not a version is stated, and chunks without a version are refused too. Only the
  /// chunked form with a version is read, which is why the id line now carries one.
  /// <para/>
  /// Flushing the deflater at the end of a row is what makes the chunk stand for exactly that row:
  /// it closes the row at a <c>00 00 FF FF</c> boundary while the stream carries on, so the chunks
  /// are cuts of one stream rather than streams of their own. The stream is deliberately never
  /// finished, because ImageMagick stops after the last row and never reads a final block.
  /// </remarks>
  private static byte[] _CompressZip(byte[] data, int bytesPerRow) {
    if (bytesPerRow <= 0)
      bytesPerRow = data.Length;

    using var deflated = new MemoryStream();
    using var payload = new MemoryStream();

    using (var zlib = new ZLibStream(deflated, CompressionLevel.SmallestSize, leaveOpen: true)) {
      var taken = 0;
      for (var at = 0; at < data.Length; at += bytesPerRow) {
        zlib.Write(data, at, Math.Min(bytesPerRow, data.Length - at));
        zlib.Flush();

        var produced = deflated.GetBuffer();
        var chunkLength = (int)deflated.Length - taken;

        var length = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)chunkLength);
        payload.Write(length);
        payload.Write(produced, taken, chunkLength);
        taken += chunkLength;
      }
    }

    return payload.ToArray();
  }
}
