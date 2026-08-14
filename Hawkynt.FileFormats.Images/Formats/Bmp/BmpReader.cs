using System;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;

namespace FileFormat.Bmp;

/// <summary>Reads BMP files from bytes, streams, or file paths.</summary>
public static class BmpReader {

  /// <summary><c>BI_BITFIELDS</c>: three masks state where each channel sits inside the pixel.</summary>
  private const int _COMPRESSION_BITFIELDS = 3;

  /// <summary><c>BI_ALPHABITFIELDS</c>: as above with a fourth mask for alpha.</summary>
  private const int _COMPRESSION_ALPHABITFIELDS = 6;

  /// <summary>The length of a BITMAPV2INFOHEADER, the shortest one carrying its own channel masks.</summary>
  private const int _V2_HEADER_SIZE = 52;

  /// <summary>The length of a BITMAPV3INFOHEADER, the shortest one carrying an alpha mask.</summary>
  private const int _V3_HEADER_SIZE = 56;

  /// <summary><c>BI_JPEG</c>: the pixel data is a JPEG stream.</summary>
  private const int _COMPRESSION_JPEG = 4;

  /// <summary><c>BI_PNG</c>: the pixel data is a PNG stream.</summary>
  private const int _COMPRESSION_PNG = 5;

  /// <summary>The same thing written as the four letters <c>JPEG</c>, which is what Konica did.</summary>
  private const int _COMPRESSION_JPEG_FOURCC = 0x4745504A;

  /// <summary>The same thing written as the four letters <c>PNG </c>.</summary>
  private const int _COMPRESSION_PNG_FOURCC = 0x20474E50;

  public static BmpFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("BMP file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BmpFile FromStream(Stream stream) {
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

  /// <summary>The length of the OS/2 BITMAPCOREHEADER, which is what marks a file as that older kind.</summary>
  private const int CORE_HEADER_SIZE = 12;

  public static BmpFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < BitmapFileHeader.StructSize + CORE_HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid BMP file.");

    // BITMAPFILEHEADER (14 bytes)
    var fileHeader = BitmapFileHeader.ReadFrom(data);
    if (fileHeader.Sig1 != (byte)'B' || fileHeader.Sig2 != (byte)'M')
      throw new InvalidDataException("Invalid BMP signature.");

    var pixelDataOffset = fileHeader.PixelDataOffset;

    // The first field of the second header states its own length, and that is what says which of
    // the two shapes follows: 12 is the OS/2 one, anything from 40 up is the Windows one.
    var headerSize = BinaryPrimitives.ReadInt32LittleEndian(data[BitmapFileHeader.StructSize..]);

    int width, rawHeight, bitsPerPixel, bmpCompression, colorsUsed, paletteEntrySize;
    if (headerSize == CORE_HEADER_SIZE) {
      // BITMAPCOREHEADER: the sizes are 16-bit, there is no compression or colour count, and the
      // palette that follows is three bytes an entry rather than four.
      var core = data[(BitmapFileHeader.StructSize + 4)..];
      width = BinaryPrimitives.ReadUInt16LittleEndian(core);
      rawHeight = BinaryPrimitives.ReadUInt16LittleEndian(core[2..]);
      bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(core[6..]);
      bmpCompression = 0;
      colorsUsed = 0;
      paletteEntrySize = 3;
    } else {
      if (headerSize < BitmapInfoHeader.StructSize || data.Length < BitmapFileHeader.StructSize + BitmapInfoHeader.StructSize)
        throw new InvalidDataException($"Unsupported BMP header size: {headerSize}.");

      var infoHeader = BitmapInfoHeader.ReadFrom(data[BitmapFileHeader.StructSize..]);
      width = infoHeader.Width;
      rawHeight = infoHeader.Height;
      bitsPerPixel = infoHeader.BitsPerPixel;
      bmpCompression = infoHeader.Compression;
      colorsUsed = infoHeader.ColorsUsed;
      paletteEntrySize = 4;
    }

    // Four and five say the pixel data is a whole JPEG or PNG stream rather than samples, and some
    // writers spell them as the four letters instead of the number — Konica's .kqp holds JPEG there.
    // Neither stream is one this reader undoes, and treating the bytes as samples anyway drew the
    // markers and entropy-coded data as a band of noise above a black field and called it a picture.
    if (bmpCompression is _COMPRESSION_JPEG or _COMPRESSION_PNG or _COMPRESSION_JPEG_FOURCC or _COMPRESSION_PNG_FOURCC)
      throw new InvalidDataException(
        $"A bitmap stating compression {bmpCompression} carries an embedded JPEG or PNG stream rather than samples.");

    var rowOrder = rawHeight < 0 ? BmpRowOrder.TopDown : BmpRowOrder.BottomUp;
    var height = Math.Abs(rawHeight);

    // Where each channel sits inside a 16- or 32-bit pixel. Under BI_BITFIELDS the file says; under
    // BI_RGB it does not, and the defaults below are what it means by saying nothing.
    //
    // A plain BITMAPINFOHEADER keeps the masks in the three (or four) words directly after itself,
    // while every header from BITMAPV2INFOHEADER up has fields of its own at offset 40 — which is the
    // same place, so one offset serves both.
    var maskOffset = BitmapFileHeader.StructSize + (headerSize >= _V2_HEADER_SIZE ? 40 : headerSize);
    var usesBitfields = bmpCompression is _COMPRESSION_BITFIELDS or _COMPRESSION_ALPHABITFIELDS;

    uint maskRed = 0, maskGreen = 0, maskBlue = 0, maskAlpha = 0;
    var alphaMaskIsStated = false;
    if (usesBitfields && data.Length >= maskOffset + 12) {
      maskRed = BinaryPrimitives.ReadUInt32LittleEndian(data[maskOffset..]);
      maskGreen = BinaryPrimitives.ReadUInt32LittleEndian(data[(maskOffset + 4)..]);
      maskBlue = BinaryPrimitives.ReadUInt32LittleEndian(data[(maskOffset + 8)..]);

      // The alpha mask is a field of its own from BITMAPV3INFOHEADER up, and the fourth word after a
      // plain header when the compression is BI_ALPHABITFIELDS. Either way it is stated, and a stated
      // zero means there is no alpha channel rather than that nobody said.
      if ((headerSize >= _V3_HEADER_SIZE || bmpCompression == _COMPRESSION_ALPHABITFIELDS)
          && data.Length >= maskOffset + 16) {
        maskAlpha = BinaryPrimitives.ReadUInt32LittleEndian(data[(maskOffset + 12)..]);
        alphaMaskIsStated = true;
      }
    }

    // A file may state BI_BITFIELDS and then leave the masks empty, which describes no channel at
    // all; fall back to what BI_RGB would have meant rather than decoding every pixel to black.
    if (maskRed == 0 && maskGreen == 0 && maskBlue == 0) {
      alphaMaskIsStated = false;
      switch (bitsPerPixel) {
        // BI_RGB at 16bpp is 5-5-5 with the top bit unused. 5-6-5 is a BI_BITFIELDS layout and only
        // when the masks say so; reading one as the other left 395 of 2257 pixels of an
        // ffmpeg-written gradient wrong, and ffprobe calls the same file rgb555le.
        case 16:
          maskRed = 0x7C00;
          maskGreen = 0x03E0;
          maskBlue = 0x001F;
          break;
        case 32:
          maskRed = 0x00FF0000;
          maskGreen = 0x0000FF00;
          maskBlue = 0x000000FF;
          break;
      }
    }

    // Skip any extra header bytes and the masks that follow a plain one.
    var paletteStart = BitmapFileHeader.StructSize + headerSize;
    if (usesBitfields && headerSize == BitmapInfoHeader.StructSize)
      paletteStart += bmpCompression == _COMPRESSION_ALPHABITFIELDS ? 16 : 12;

    // Read palette
    byte[]? palette = null;
    var paletteColorCount = 0;
    if (bitsPerPixel <= 8) {
      paletteColorCount = colorsUsed > 0 ? colorsUsed : 1 << bitsPerPixel;

      // A file may state more entries than it carries; keep to what is actually there.
      var available = (pixelDataOffset > paletteStart ? pixelDataOffset - paletteStart : data.Length - paletteStart) / paletteEntrySize;
      if (available > 0 && paletteColorCount > available)
        paletteColorCount = available;

      palette = new byte[paletteColorCount * 3];
      var paletteOffset = paletteStart;
      for (var i = 0; i < paletteColorCount; ++i) {
        palette[i * 3] = data[paletteOffset + 2];     // R (from BGR+reserved)
        palette[i * 3 + 1] = data[paletteOffset + 1]; // G
        palette[i * 3 + 2] = data[paletteOffset];     // B
        paletteOffset += paletteEntrySize;
      }
    }

    // Read pixel data
    var remainingBytes = data.Length - pixelDataOffset;
    var rawPixelData = new byte[remainingBytes];
    data.Slice(pixelDataOffset, remainingBytes).CopyTo(rawPixelData.AsSpan(0));

    var compression = bmpCompression switch {
      1 => BmpCompression.Rle8,
      2 => BmpCompression.Rle4,
      _ => BmpCompression.None
    };

    var colorMode = _DetectColorMode(bitsPerPixel, palette, paletteColorCount);

    byte[] pixelData;
    if (compression == BmpCompression.Rle8) {
      pixelData = RleCompressor.DecompressRle8(rawPixelData, width, height);
    } else {
      // A 4-bit run-length picture used to fall straight through here, which read its opcodes as
      // pixels and drew noise; the writer has been able to produce these all along and nothing could
      // read one back. Unpacking it into the rows an uncompressed one would have had keeps the
      // ordering and the un-padding below as one path rather than two.
      if (compression == BmpCompression.Rle4)
        rawPixelData = RleCompressor.DecompressRle4(rawPixelData, width, height);

      var bytesPerRow = (width * bitsPerPixel + 7) / 8;
      var paddedBytesPerRow = (bytesPerRow + 3) & ~3;
      pixelData = new byte[bytesPerRow * height];
      for (var row = 0; row < height; ++row) {
        var srcOffset = row * paddedBytesPerRow;
        var dstRow = rowOrder == BmpRowOrder.BottomUp ? height - 1 - row : row;
        var dstOffset = dstRow * bytesPerRow;
        if (srcOffset + bytesPerRow <= rawPixelData.Length)
          rawPixelData.AsSpan(srcOffset, bytesPerRow).CopyTo(pixelData.AsSpan(dstOffset));
      }
      // After de-ordering, data is in top-down order
      rowOrder = BmpRowOrder.TopDown;

      // A RawImage in a sub-byte format runs its indices straight on across the picture, where BMP
      // starts every row on a byte boundary. The two agree for any width that is a multiple of eight
      // pixels, which is nearly every picture — and diverge by the padding bits for the rest, putting
      // every row after the first further out of step than the one above it. A 196 by 228 one-bit
      // file came out 71% right against XnView and ImageMagick, which agree with each other exactly.
      pixelData = _RemoveRowPadding(pixelData, width, height, bitsPerPixel);
    }

    // A 16- or 32-bit pixel is a packed word whose channels the masks locate. Widening it here rather
    // than downstream keeps BitsPerPixel describing what PixelData actually holds, which is what the
    // stride arithmetic in BmpFile.ToRawImage reads it for.
    if (bitsPerPixel is 16 or 32 && compression == BmpCompression.None) {
      var carriesAlpha = _CarriesAlpha(
        pixelData, width * height, bitsPerPixel, ref maskAlpha, alphaMaskIsStated);

      pixelData = _ExpandMaskedSamples(
        pixelData, width * height, bitsPerPixel, maskRed, maskGreen, maskBlue, maskAlpha, carriesAlpha);

      bitsPerPixel = carriesAlpha ? 32 : 24;
      colorMode = carriesAlpha ? BmpColorMode.Bgra32 : BmpColorMode.Rgb24;
    }

    return new BmpFile {
      Width = width,
      Height = height,
      BitsPerPixel = bitsPerPixel,
      PixelData = pixelData,
      Palette = palette,
      PaletteColorCount = paletteColorCount,
      RowOrder = rowOrder,
      Compression = compression,
      ColorMode = colorMode
    };
  }

  public static BmpFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Restacks sub-byte rows from BMP's byte-aligned layout to the continuous one.</summary>
  private static byte[] _RemoveRowPadding(byte[] padded, int width, int height, int bitsPerPixel) {
    if (bitsPerPixel >= 8)
      return padded;

    var paddedStride = (width * bitsPerPixel + 7) / 8;
    if (paddedStride * 8 == width * bitsPerPixel)
      return padded;

    var result = new byte[(width * height * bitsPerPixel + 7) / 8];
    var mask = (1 << bitsPerPixel) - 1;

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var sourceBit = y * paddedStride * 8 + x * bitsPerPixel;
      var sourceByte = sourceBit >> 3;
      if (sourceByte >= padded.Length)
        return result;

      var value = (padded[sourceByte] >> (8 - bitsPerPixel - (sourceBit & 7))) & mask;
      var targetBit = (y * width + x) * bitsPerPixel;
      result[targetBit >> 3] |= (byte)(value << (8 - bitsPerPixel - (targetBit & 7)));
    }

    return result;
  }

  /// <summary>Decides whether a 16- or 32-bit file's spare bits are an alpha channel or padding.</summary>
  /// <remarks>
  /// <c>biCompression</c> alone does not say, so this was settled by measurement against ffmpeg n9.0
  /// and ImageMagick 7.1.2 over bitmaps built to isolate each case:
  /// <list type="bullet">
  /// <item>32bpp BI_RGB whose fourth byte is 0x80 throughout, or varies across the row: both tools
  /// read it as alpha and keep the value.</item>
  /// <item>32bpp BI_RGB whose fourth byte is zero throughout: ffmpeg substitutes an opaque alpha and
  /// ImageMagick drops the channel. Both render it opaque, and so do we — writers that mean the byte
  /// as padding leave it at zero, and taking that literally turns an opaque picture invisible.</item>
  /// <item>A stated alpha mask of zero (BITMAPV4HEADER, BI_BITFIELDS): ffmpeg reports <c>bgr0</c> and
  /// ImageMagick three channels. No alpha.</item>
  /// <item>A stated non-zero alpha mask whose every pixel is zero: the two disagree. ImageMagick
  /// honours the mask and gives a transparent picture; ffmpeg applies the same rescue it uses for
  /// BI_RGB and gives an opaque one. We follow ImageMagick, because a header that declares an alpha
  /// mask has stated something and overriding a stated channel is how a legitimately transparent
  /// picture becomes a wrong one — whereas the rescue above fills a gap where nothing was stated.</item>
  /// </list>
  /// 16bpp gets no implicit alpha under either tool, so the spare bit of a 5-5-5 stays spare.
  /// </remarks>
  private static bool _CarriesAlpha(
    byte[] packed, int pixelCount, int bitsPerPixel, ref uint maskAlpha, bool alphaMaskIsStated) {
    if (alphaMaskIsStated)
      return maskAlpha != 0;

    // Nothing was stated. Only a 32-bit pixel has room left over once three 8-bit channels are placed.
    if (bitsPerPixel != 32)
      return false;

    maskAlpha = 0xFF000000;
    return _AnyMaskedBitSet(packed, pixelCount, bitsPerPixel, maskAlpha);
  }

  private static bool _AnyMaskedBitSet(byte[] packed, int pixelCount, int bitsPerPixel, uint mask) {
    var bytesPerPixel = bitsPerPixel / 8;
    for (var i = 0; i < pixelCount; ++i)
      if ((_ReadPacked(packed, i, bytesPerPixel) & mask) != 0)
        return true;

    return false;
  }

  private static uint _ReadPacked(byte[] packed, int index, int bytesPerPixel) {
    var offset = index * bytesPerPixel;
    if (offset + bytesPerPixel > packed.Length)
      return 0;

    return bytesPerPixel == 2
      ? BinaryPrimitives.ReadUInt16LittleEndian(packed.AsSpan(offset))
      : BinaryPrimitives.ReadUInt32LittleEndian(packed.AsSpan(offset));
  }

  /// <summary>Widens packed masked channels into one byte each, blue-green-red(-alpha).</summary>
  private static byte[] _ExpandMaskedSamples(
    byte[] packed, int pixelCount, int bitsPerPixel,
    uint maskRed, uint maskGreen, uint maskBlue, uint maskAlpha, bool withAlpha) {
    var bytesPerPixel = bitsPerPixel / 8;
    var bytesPerSample = withAlpha ? 4 : 3;
    var result = new byte[pixelCount * bytesPerSample];

    var red = _MaskShape(maskRed);
    var green = _MaskShape(maskGreen);
    var blue = _MaskShape(maskBlue);
    var alpha = _MaskShape(maskAlpha);

    for (var i = 0; i < pixelCount; ++i) {
      var value = _ReadPacked(packed, i, bytesPerPixel);
      var destination = i * bytesPerSample;
      result[destination] = _Sample(value, blue);
      result[destination + 1] = _Sample(value, green);
      result[destination + 2] = _Sample(value, red);
      if (withAlpha)
        result[destination + 3] = alpha.Width == 0 ? (byte)0xFF : _Sample(value, alpha);
    }

    return result;
  }

  private static (uint Mask, int Shift, int Width) _MaskShape(uint mask)
    => mask == 0 ? (0u, 0, 0) : (mask, BitOperations.TrailingZeroCount(mask), BitOperations.PopCount(mask));

  /// <summary>Widens one channel to the full 0..255 range by repeating its bits.</summary>
  /// <remarks>
  /// Sweeping all 32 values of a 5-bit channel through both tools settled which of the two candidate
  /// rules they use. ffmpeg matches bit replication on 32 of 32; rounding the scale instead differs
  /// from it at four values (3, 7, 24, 28) and is what left 488 of 2257 pixels of the gradient off by
  /// one. ImageMagick matches neither cleanly — 30 of 32 either way, disagreeing with ffmpeg at 3 and
  /// 7 — so the two tools differ from each other on 366 pixels of that same file and no single answer
  /// satisfies both. We follow ffmpeg, which is the reading the parity check measures against and the
  /// only one of the two that is self-consistent.
  /// <para/>
  /// Replication is also the rule that reaches the ends of the range: a full-scale 5-bit 31 becomes
  /// 255 rather than the 248 a plain shift gives. The one place it parts company with both tools is a
  /// 4-4-4 layout, where each of them shifts instead and so cannot express white — a full-scale 15
  /// comes back as 240 from both. That layout is vanishingly rare and being unable to write white is
  /// the worse fault, so it is not followed there.
  /// </remarks>
  private static byte _Sample(uint value, (uint Mask, int Shift, int Width) channel) {
    if (channel.Width == 0)
      return 0;

    var raw = (value & channel.Mask) >> channel.Shift;
    if (channel.Width >= 8)
      return (byte)(raw >> (channel.Width - 8));

    // Repeat the pattern until it fills at least eight bits, then keep the top eight.
    var filled = raw;
    var bits = channel.Width;
    while (bits < 8) {
      filled = (filled << channel.Width) | raw;
      bits += channel.Width;
    }

    return (byte)(filled >> (bits - 8));
  }

  private static BmpColorMode _DetectColorMode(int bitsPerPixel, byte[]? palette, int paletteColorCount) {
    if (bitsPerPixel == 24)
      return BmpColorMode.Rgb24;

    if (bitsPerPixel == 8 && palette != null) {
      var isGray = true;
      for (var i = 0; i < paletteColorCount; ++i) {
        if (palette[i * 3] != palette[i * 3 + 1] || palette[i * 3 + 1] != palette[i * 3 + 2]) {
          isGray = false;
          break;
        }
      }

      return isGray ? BmpColorMode.Grayscale8 : BmpColorMode.Palette8;
    }

    if (bitsPerPixel == 4)
      return BmpColorMode.Palette4;

    if (bitsPerPixel == 1)
      return BmpColorMode.Palette1;

    return BmpColorMode.Original;
  }
}
