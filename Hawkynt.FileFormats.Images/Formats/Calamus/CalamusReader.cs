using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Calamus;

/// <summary>Reads Calamus raster image files from bytes, streams, or file paths.</summary>
public static class CalamusReader {

  public static CalamusFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Calamus file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CalamusFile FromStream(Stream stream) {
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

  public static CalamusFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < CalamusFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid Calamus file (need at least {CalamusFile.MinFileSize} bytes, got {data.Length}).");

    if (data.Length > CalamusFile.RasterDataOffset && data[..CalamusFile.RasterMagic.Length].SequenceEqual(CalamusFile.RasterMagic))
      return _ReadRasterGraphic(data);

    if (data[0] != CalamusFile.Magic[0] || data[1] != CalamusFile.Magic[1] || data[2] != CalamusFile.Magic[2] || data[3] != CalamusFile.Magic[3])
      throw new InvalidDataException("Invalid Calamus magic bytes.");

    var header = CalamusHeader.ReadFrom(data);
    var version = header.Version;
    var width = header.Width;
    var height = header.Height;
    var bpp = header.Bpp;

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid Calamus dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < CalamusFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("Calamus file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.Slice(CalamusFile.HeaderSize, pixelDataSize).CopyTo(pixelData);

    return new() {
      Width = width,
      Height = height,
      Version = version,
      Bpp = bpp,
      PixelData = pixelData,
    };
  }

  /// <summary>
  /// Reads a Calamus raster graphic: a 32-byte header, a 10-byte chunk header, then a packed screen.
  /// </summary>
  /// <remarks>
  /// The size is stated in long words rather than words — 20 for the width, 24 for the height and 28
  /// for the bytes a row takes, which is what a picture 251 wide needs 32 of.
  /// <para/>
  /// The packing: a control byte with its top bit set repeats the byte after it, one more time than
  /// the low seven bits say; one without counts the bytes that follow, again one more than it says,
  /// and they are taken as they stand.
  /// <para/>
  /// Worked out by rebuilding the pictures RECOIL draws and reading the files against them. All three
  /// samples expand to exactly the bytes their stated size takes — 6432, 32000 and 32000 — and match
  /// RECOIL pixel for pixel, with two bytes left over at the end of each that are no part of the
  /// picture.
  /// </remarks>
  private static CalamusFile _ReadRasterGraphic(ReadOnlySpan<byte> data) {
    var width = BinaryPrimitives.ReadInt32BigEndian(data[CalamusFile.RasterWidthOffset..]);
    var height = BinaryPrimitives.ReadInt32BigEndian(data[(CalamusFile.RasterWidthOffset + 4)..]);
    var bytesPerRow = BinaryPrimitives.ReadInt32BigEndian(data[(CalamusFile.RasterWidthOffset + 8)..]);

    if (width <= 0 || height <= 0 || bytesPerRow <= 0 || width > 0xFFFF || height > 0xFFFF)
      throw new InvalidDataException($"A Calamus raster graphic of {width}x{height} in rows of {bytesPerRow} is no picture.");

    var wanted = bytesPerRow * height;
    var pixelData = new byte[wanted];
    var written = 0;
    var pos = CalamusFile.RasterDataOffset;

    while (pos < data.Length && written < wanted) {
      var control = data[pos++];
      if (pos >= data.Length)
        break;

      if ((control & 0x80) != 0) {
        var run = Math.Min((control & 0x7F) + 1, wanted - written);
        pixelData.AsSpan(written, run).Fill(data[pos++]);
        written += run;
        continue;
      }

      var literal = Math.Min(control + 1, Math.Min(data.Length - pos, wanted - written));
      data.Slice(pos, literal).CopyTo(pixelData.AsSpan(written));
      pos += control + 1;
      written += literal;
    }

    if (written < wanted)
      throw new InvalidDataException($"A Calamus raster graphic of {width}x{height} holds {wanted} bytes; this one ran out after {written}.");

    return new() {
      Width = width,
      Height = height,
      Version = 0,
      Bpp = 1,
      PixelData = pixelData,
    };
  }

  public static CalamusFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
