using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.AtariGfb;

/// <summary>Reads DeskPic pictures from bytes, streams, or file paths.</summary>
public static class AtariGfbReader {

  public static AtariGfbFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("DeskPic file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariGfbFile FromStream(Stream stream) {
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

  public static AtariGfbFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < AtariGfbFile.HeaderSize
        || Encoding.ASCII.GetString(data[..4]) != AtariGfbFile.Signature)
      throw new InvalidDataException("Not a DeskPic picture: wrong signature.");

    // The header states how many colours rather than how many planes, and only the four counts that
    // are a whole number of planes are a picture.
    var bitplanes = BinaryPrimitives.ReadInt32BigEndian(data[4..]) switch {
      2 => 1,
      4 => 2,
      16 => 4,
      256 => 8,
      var other => throw new InvalidDataException($"A DeskPic picture of {other} colours is not one this reads."),
    };

    var width = BinaryPrimitives.ReadInt32BigEndian(data[8..]);
    var height = BinaryPrimitives.ReadInt32BigEndian(data[12..]);
    var bitmapLength = BinaryPrimitives.ReadInt32BigEndian(data[16..]);

    if (width <= 0 || height <= 0 || bitmapLength <= 0)
      throw new InvalidDataException($"A DeskPic picture of {width}x{height} in {bitmapLength} bytes is not one.");

    if (bitmapLength != AtariGfbFile.Stride(width, bitplanes) * height)
      throw new InvalidDataException(
        $"A DeskPic {width}x{height} of {bitplanes} planes needs {AtariGfbFile.Stride(width, bitplanes) * height} "
        + $"bytes of bitmap, but its header says {bitmapLength}.");

    var expected = AtariGfbFile.HeaderSize + bitmapLength + AtariGfbFile.PaletteSize;
    if (data.Length != expected)
      throw new InvalidDataException($"A DeskPic picture of this shape is {expected} bytes, got {data.Length}.");

    return new() {
      Width = width,
      Height = height,
      Bitplanes = bitplanes,
      PixelData = AtariStGraphics.UnpackBitplanes(
        data, AtariGfbFile.HeaderSize, AtariGfbFile.Stride(width, bitplanes), bitplanes, width, height),
      Palette = AtariStGraphics.ReadVdiPalette(
        data, AtariGfbFile.HeaderSize + bitmapLength, 1 << bitplanes, bitplanes),
    };
  }

  public static AtariGfbFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
