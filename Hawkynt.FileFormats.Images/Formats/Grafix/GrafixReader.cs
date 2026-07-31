using System;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Grafix;

/// <summary>Reads Grafix pictures from bytes, streams, or file paths.</summary>
public static class GrafixReader {

  public static GrafixFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GrafixFile FromStream(Stream stream) {
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

  public static GrafixFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 1588 || Encoding.ASCII.GetString(data[..GrafixFile.Signature.Length]) != GrafixFile.Signature
        || data[4] != 1 || data[5] != 1 || data[28] != 0)
      throw new InvalidDataException("Not a Grafix picture.");

    var width = (data[30] << 8) | data[31];
    var height = (data[32] << 8) | data[33];

    // The header names the colour count rather than the plane count.
    var planes = ((data[34] << 8) | data[35]) switch {
      2 => 1,
      4 => 2,
      16 => 4,
      256 => 8,
      _ => throw new InvalidDataException("A Grafix picture has 2, 4, 16 or 256 colours."),
    };

    var stride = AtariStGraphics.BytesPerRow(width, planes);
    var bitmapLength = _BigEndian(data, 1574);
    if (width == 0 || height == 0 || bitmapLength != stride * height)
      throw new InvalidDataException($"A {width}x{height} picture of {planes} planes is not {bitmapLength} bytes.");

    var bitmap = data[29] switch {
      0 => _Raw(data, bitmapLength),
      1 => _Unpack(data, bitmapLength),
      _ => throw new InvalidDataException($"A Grafix picture is packed or not, and not {data[29]}."),
    };

    return new() {
      Bitmap = bitmap,
      Palette = AtariStGraphics.ReadVdiPalette(data, GrafixFile.PaletteOffset, 1 << planes, planes),
      Width = width,
      Height = height,
      Planes = planes,
    };
  }

  private static byte[] _Raw(ReadOnlySpan<byte> data, int bitmapLength) {
    if (data.Length != GrafixFile.BitmapOffset + bitmapLength)
      throw new InvalidDataException($"An unpacked Grafix picture does not fit {data.Length} bytes.");

    return data.Slice(GrafixFile.BitmapOffset, bitmapLength).ToArray();
  }

  /// <summary>Unpacks the two halves the packed form splits its picture into.</summary>
  private static byte[] _Unpack(ReadOnlySpan<byte> data, int bitmapLength) {
    var firstLength = _BigEndian(data, 1578);
    if (firstLength <= 0)
      throw new InvalidDataException("A packed Grafix picture declares an empty first half.");

    var secondOffset = GrafixFile.BitmapOffset + firstLength;
    if (data.Length != secondOffset + _BigEndian(data, 1582))
      throw new InvalidDataException("A packed Grafix picture's halves do not account for the file.");

    var bitmap = new byte[bitmapLength];
    var half = bitmapLength >> 1;
    GrafixLzw.Unpack(data, GrafixFile.BitmapOffset, secondOffset, bitmap, 0, half);
    GrafixLzw.Unpack(data, secondOffset, data.Length, bitmap, half, bitmapLength);

    return bitmap;
  }

  private static int _BigEndian(ReadOnlySpan<byte> data, int offset)
    => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

  public static GrafixFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
