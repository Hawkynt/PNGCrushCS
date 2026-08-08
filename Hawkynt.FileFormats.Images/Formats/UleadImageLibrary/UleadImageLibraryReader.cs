using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Jpeg;

namespace FileFormat.UleadImageLibrary;

/// <summary>Reads Ulead image libraries from bytes, streams, or file paths.</summary>
public static class UleadImageLibraryReader {

  private static ReadOnlySpan<byte> JpegStart => [0xFF, 0xD8, 0xFF];
  private static ReadOnlySpan<byte> JpegEnd => [0xFF, 0xD9];

  public static UleadImageLibraryFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Ulead image library not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static UleadImageLibraryFile FromStream(Stream stream) {
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

  public static UleadImageLibraryFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < UleadImageLibraryFile.FirstRecordBase)
      throw new InvalidDataException($"Data too small for a Ulead image library (got {data.Length} bytes).");

    if (!data[..UleadImageLibraryFile.Magic.Length].SequenceEqual(UleadImageLibraryFile.Magic))
      throw new InvalidDataException("Not a Ulead image library: it does not open the way one does.");

    var count = BinaryPrimitives.ReadInt32LittleEndian(data[UleadImageLibraryFile.ItemCountAt..]);
    if (count < 1 || count > UleadImageLibraryFile.MaximumItems)
      throw new InvalidDataException($"A Ulead image library states {count} items.");

    var at = UleadImageLibraryFile.FirstRecordBase + 4 * count;
    var items = new List<byte[]>(count);

    for (var i = 0; i < count; ++i) {
      // Records are padded apart with whole words of zero in one sample; padding is skipped and
      // nothing is searched for, the next record having to begin where the zeros stop.
      while (at + 4 <= data.Length && BinaryPrimitives.ReadUInt32LittleEndian(data[at..]) == 0)
        at += 4;

      if (at + UleadImageLibraryFile.RecordHeaderSize > data.Length)
        throw new InvalidDataException($"A Ulead image library states {count} items and the file ends at item {i}.");

      var type = BinaryPrimitives.ReadInt32LittleEndian(data[(at + UleadImageLibraryFile.RecordTypeAt)..]);
      if (type != UleadImageLibraryFile.RecordType)
        throw new InvalidDataException($"Item {i} of a Ulead image library states type {type} where every record states {UleadImageLibraryFile.RecordType}.");

      var extraLength = BinaryPrimitives.ReadInt32LittleEndian(data[(at + UleadImageLibraryFile.ExtraLengthAt)..]);
      var width = BinaryPrimitives.ReadInt32LittleEndian(data[(at + UleadImageLibraryFile.WidthAt)..]);
      var height = BinaryPrimitives.ReadInt32LittleEndian(data[(at + UleadImageLibraryFile.HeightAt)..]);
      var jpegLength = BinaryPrimitives.ReadInt32LittleEndian(data[(at + UleadImageLibraryFile.JpegLengthAt)..]);

      if (extraLength < 0 || jpegLength < JpegStart.Length + JpegEnd.Length || width < 1 || height < 1)
        throw new InvalidDataException($"Item {i} of a Ulead image library states {width}x{height} in {jpegLength} bytes.");

      var start = at + UleadImageLibraryFile.RecordHeaderSize;
      if (start + (long)jpegLength > data.Length)
        throw new InvalidDataException($"Item {i} of a Ulead image library states {jpegLength} bytes of picture and the file has {data.Length - start}.");

      var jpeg = data.Slice(start, jpegLength);

      // The stated length landing exactly on the picture's own end marker is what says the walk is
      // where the format means it to be. A parse off by any amount misses it.
      if (!jpeg[..JpegStart.Length].SequenceEqual(JpegStart) || !jpeg[^JpegEnd.Length..].SequenceEqual(JpegEnd))
        throw new InvalidDataException($"Item {i} of a Ulead image library states {jpegLength} bytes that do not begin and end a JPEG.");

      var embedded = jpeg.ToArray();

      // And the size the record states has to be the size the picture states of itself, so a length
      // that happens to bracket a JPEG cannot pass for the record it belongs to.
      var decoded = JpegReader.FromBytes(embedded);
      if (decoded.Width != width || decoded.Height != height)
        throw new InvalidDataException(
          $"Item {i} of a Ulead image library says {width}x{height} and the JPEG it carries is {decoded.Width}x{decoded.Height}.");

      items.Add(embedded);
      at = start + jpegLength + UleadImageLibraryFile.MetadataSize + extraLength;
    }

    // Whatever is left is the slack the writer padded with, and it is zero in all ten samples.
    for (var tail = at; tail < data.Length; ++tail)
      if (data[tail] != 0)
        throw new InvalidDataException($"A Ulead image library ends with {data.Length - at} bytes that are not the padding these end with.");

    return new() { Items = items };
  }

  public static UleadImageLibraryFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
