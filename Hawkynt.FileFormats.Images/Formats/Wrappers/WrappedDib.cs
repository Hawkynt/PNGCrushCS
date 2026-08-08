using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Wrappers;

/// <summary>
/// Decodes a Windows device-independent bitmap that sits at a known place inside another file.
/// </summary>
/// <remarks>
/// A DIB is a BMP without its file header: a <c>BITMAPINFOHEADER</c>, the palette, then the rows
/// bottom-up and padded to four bytes. Containers that carry a preview or a thumbnail store exactly
/// that, so the run is handed to the bitmap reader with a file header put in front of it and every
/// depth, palette form and row order that reader already knows comes free.
/// <para/>
/// <see cref="FileFormat.EmbeddedDib"/> searches a file for one of these. This is for the formats that
/// say where theirs is, where being told is stronger evidence than finding — a stated offset that
/// holds a header whose stated length fits the file is a fact about the format, not a coincidence.
/// </remarks>
internal static class WrappedDib {

  /// <summary>The shortest and longest <c>BITMAPINFOHEADER</c> this accepts.</summary>
  internal const int MinHeaderSize = 40, MaxHeaderSize = 124;

  /// <summary>How many bytes the whole bitmap at <paramref name="at"/> takes, header and palette and rows.</summary>
  /// <remarks>Returns -1 when there is no bitmap header there or when what it states will not fit.</remarks>
  internal static int Measure(ReadOnlySpan<byte> data, int at, int maxDimension) {
    if (at < 0 || at + MinHeaderSize > data.Length)
      return -1;

    var size = BinaryPrimitives.ReadInt32LittleEndian(data[at..]);
    if (size is < MinHeaderSize or > MaxHeaderSize || at + size > data.Length)
      return -1;

    var width = BinaryPrimitives.ReadInt32LittleEndian(data[(at + 4)..]);
    var height = BinaryPrimitives.ReadInt32LittleEndian(data[(at + 8)..]);
    var planes = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 12)..]);
    var bits = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 14)..]);
    var compression = BinaryPrimitives.ReadInt32LittleEndian(data[(at + 16)..]);
    if (planes != 1 || bits is not (1 or 4 or 8 or 16 or 24 or 32) || compression is not (0 or 1 or 2))
      return -1;

    // Height is signed: negative means the rows run top-down.
    var rows = Math.Abs((long)height);
    if (width < 1 || width > maxDimension || rows < 1 || rows > maxDimension)
      return -1;

    // A packed picture states its own length; an unpacked one is a stride times its rows.
    var packed = BinaryPrimitives.ReadInt32LittleEndian(data[(at + 20)..]);
    var stride = ((long)width * bits + 31) / 32 * 4;
    var payload = compression == 0 || packed <= 0 ? stride * rows : packed;
    var total = PixelOffset(data, at) + payload;

    return at + total > data.Length ? -1 : (int)total;
  }

  /// <summary>Bytes from the header to the picture: the header itself and the palette after it.</summary>
  internal static int PixelOffset(ReadOnlySpan<byte> data, int at) {
    var size = BinaryPrimitives.ReadInt32LittleEndian(data[at..]);
    var bits = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 14)..]);
    if (bits > 8)
      return size;

    var used = BinaryPrimitives.ReadInt32LittleEndian(data[(at + 32)..]);
    if (used is <= 0 or > 256)
      used = 1 << bits;

    return size + used * 4;
  }

  /// <summary>A picture as the bitmap one of these containers stores: the info header, the palette
  /// and the rows, with no file header in front of it.</summary>
  /// <remarks>
  /// The inverse of <see cref="Decode"/>, and it goes through the same bitmap writer the reader hands
  /// its bytes to — so the palette form, the row order and the padding are whatever that writer and
  /// that reader already agree on rather than a second opinion about them kept here.
  /// </remarks>
  internal static byte[] Encode(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var bmp = BmpWriter.ToBytes(BmpFile.FromRawImage(image));
    if (bmp.Length <= 14)
      throw new InvalidDataException("The bitmap writer produced nothing to embed.");

    return bmp[14..];
  }

  /// <summary>Decodes the bitmap at <paramref name="at"/>, or throws saying why it is not one.</summary>
  internal static RawImage Decode(ReadOnlySpan<byte> data, int at, int maxDimension, string what) {
    var length = Measure(data, at, maxDimension);
    if (length < 0)
      throw new InvalidDataException($"{what} states a bitmap at {at} that the file cannot hold.");

    var bmp = new byte[14 + length];
    bmp[0] = (byte)'B';
    bmp[1] = (byte)'M';
    BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2), bmp.Length);
    BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), 14 + PixelOffset(data, at));
    data.Slice(at, length).CopyTo(bmp.AsSpan(14));

    return BmpFile.ToRawImage(BmpReader.FromSpan(bmp));
  }
}
