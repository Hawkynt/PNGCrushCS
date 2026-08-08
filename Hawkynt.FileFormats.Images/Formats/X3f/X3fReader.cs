using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.X3f;

/// <summary>Reads Sigma/Foveon X3F files from bytes, streams, or file paths.</summary>
public static class X3fReader {

  /// <summary>More sections than any of these has, and it keeps a false match cheap.</summary>
  private const int _MaxSections = 256;

  /// <summary>
  /// How much of the size the header claims a section has to reach before it counts as the picture.
  /// </summary>
  /// <remarks>
  /// The full-size JPEG in a Polaroid x530 is 1408 by 1056 against a stated 1420 by 1060, so it
  /// clears this comfortably; the largest thing readable in a Sigma SD10 without undoing the Foveon
  /// coding is 189 by 126 against a stated 2268 by 1512, which does not come close. Half is far
  /// below anything a maker would call a full-size image and far above any preview.
  /// </remarks>
  private const int _FullSizeNumerator = 1;
  private const int _FullSizeDenominator = 2;

  public static X3fFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("X3F file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static X3fFile FromStream(Stream stream) {
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

  public static X3fFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < X3fFile.HeaderSize + 4 || !data[..4].SequenceEqual(X3fFile.Magic))
      throw new InvalidDataException("Not an X3F file: it does not open with FOVb.");

    var statedWidth = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[X3fFile.ColumnsField..]);
    var statedHeight = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[X3fFile.RowsField..]);

    var directory = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(data.Length - 4)..]);
    if (directory < X3fFile.HeaderSize || directory + 12 > data.Length - 4
        || !data.Slice(directory, 4).SequenceEqual(X3fFile.DirectoryMagic))
      throw new InvalidDataException("An X3F file points at a directory that is not one.");

    var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(directory + 8)..]);
    if (count is < 1 or > _MaxSections)
      throw new InvalidDataException($"An X3F directory states {count} sections.");

    if (directory + 12 + (long)count * X3fFile.DirectoryEntrySize > data.Length)
      throw new InvalidDataException($"An X3F directory of {count} sections does not fit in {data.Length} bytes.");

    RawImage? best = null;
    var refused = 0;

    for (var i = 0; i < count; ++i) {
      var at = directory + 12 + i * X3fFile.DirectoryEntrySize;
      var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
      var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 4)..]);

      // IMAG is what the older files call a picture section and IMA2 what the newer ones do; the
      // rest of the kinds are camera settings and properties.
      if (!_IsImageSection(data.Slice(at + 8, 4)))
        continue;

      if (offset < X3fFile.HeaderSize || length < X3fFile.ImageSectionHeaderSize || (long)offset + length > data.Length)
        throw new InvalidDataException($"An X3F section states {length} bytes at {offset}, which the file cannot hold.");

      var section = data.Slice(offset, length);
      var format = (int)BinaryPrimitives.ReadUInt32LittleEndian(section[12..]);
      var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(section[16..]);
      var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(section[20..]);
      var stride = (int)BinaryPrimitives.ReadUInt32LittleEndian(section[24..]);
      var body = section[X3fFile.ImageSectionHeaderSize..];

      var decoded = format switch {
        X3fFile.FormatJpeg => _ReadJpeg(body),
        X3fFile.FormatRgb24 => _ReadRgb24(body, width, height, stride),
        _ => null,
      };

      if (decoded == null) {
        ++refused;
        continue;
      }

      if (best == null || (long)decoded.Width * decoded.Height > (long)best.Width * best.Height)
        best = decoded;
    }

    if (best == null)
      throw new InvalidDataException(
        refused > 0
          ? $"An X3F file stores its {refused} picture sections in a Foveon coding this does not undo."
          : "An X3F file carries no picture section.");

    // The header says how big the camera says the picture is. Anything far short of that is one of
    // the previews these carry alongside the sensor data, and drawing it would answer the wrong
    // question confidently.
    if (statedWidth > 0 && statedHeight > 0
        && ((long)best.Width * _FullSizeDenominator < (long)statedWidth * _FullSizeNumerator
            || (long)best.Height * _FullSizeDenominator < (long)statedHeight * _FullSizeNumerator))
      throw new InvalidDataException(
        $"An X3F file states {statedWidth} by {statedHeight} and the largest picture this can read "
        + $"from it is {best.Width} by {best.Height}, which is a preview and not the picture.");

    var rgb = PixelConverter.Convert(best, PixelFormat.Rgb24);

    return new() {
      Width = rgb.Width,
      Height = rgb.Height,
      PixelData = rgb.PixelData,
    };
  }

  public static X3fFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static bool _IsImageSection(ReadOnlySpan<byte> kind)
    => kind[0] == 'I' && kind[1] == 'M' && kind[2] == 'A' && kind[3] is (byte)'G' or (byte)'2';

  private static RawImage? _ReadJpeg(ReadOnlySpan<byte> body) {
    if (body.Length < 4 || body[0] != 0xFF || body[1] != 0xD8)
      return null;

    try {
      return JpegFile.ToRawImage(JpegReader.FromBytes(body.ToArray()));
    } catch (InvalidDataException) {
      return null;
    }
  }

  private static RawImage? _ReadRgb24(ReadOnlySpan<byte> body, int width, int height, int stride) {
    if (width < 1 || height < 1)
      return null;

    // The stride is stated and is not always the width times three — one of these pads each row by a
    // byte — so rows are taken at the stated stride rather than packed end to end.
    if (stride < width * 3)
      stride = width * 3;

    if ((long)stride * height > body.Length)
      return null;

    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      body.Slice(y * stride, width * 3).CopyTo(pixels.AsSpan(y * width * 3));

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }
}
