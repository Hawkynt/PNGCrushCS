using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.SecondNatureSlideShow;

/// <summary>Reads Second Nature slide show collections from bytes, streams, or file paths.</summary>
public static class SecondNatureSlideShowReader {

  /// <summary>Where the collection's title sits in the header.</summary>
  private const int _TitleOffset = 0x50, _TitleLength = 0x80;

  public static SecondNatureSlideShowFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Second Nature collection not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SecondNatureSlideShowFile FromStream(Stream stream) {
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

  public static SecondNatureSlideShowFile FromSpan(ReadOnlySpan<byte> data) {
    var signature = Encoding.ASCII.GetBytes(SecondNatureSlideShowFile.Signature);
    if (data.Length < SecondNatureSlideShowFile.DirectoryOffset + SecondNatureSlideShowFile.DirectoryEntrySize
        || !data[..signature.Length].SequenceEqual(signature))
      throw new InvalidDataException("Not a Second Nature collection: it does not open with the Slide Show Collection line.");

    // The first word says where the slides begin, and the directory is everything between it and
    // them. That is what states how many there are, so a count that does not divide evenly says
    // these bytes are not a directory.
    var first = BinaryPrimitives.ReadInt32LittleEndian(data[SecondNatureSlideShowFile.DirectoryOffset..]);
    var span = first - SecondNatureSlideShowFile.DirectoryOffset;
    if (span < SecondNatureSlideShowFile.DirectoryEntrySize || first > data.Length || span % SecondNatureSlideShowFile.DirectoryEntrySize != 0)
      throw new InvalidDataException($"A Second Nature collection states its slides begin at {first}, which leaves no whole directory.");

    var count = span / SecondNatureSlideShowFile.DirectoryEntrySize;
    if (count > SecondNatureSlideShowFile.MaxSlides)
      throw new InvalidDataException($"A Second Nature collection states {count} slides.");

    var slides = new List<SecondNatureSlide>(count);
    var expected = first;
    for (var i = 0; i < count; ++i) {
      var at = SecondNatureSlideShowFile.DirectoryOffset + i * SecondNatureSlideShowFile.DirectoryEntrySize;
      var offset = BinaryPrimitives.ReadInt32LittleEndian(data[at..]);
      var length = BinaryPrimitives.ReadInt32LittleEndian(data[(at + 4)..]);

      if (offset != expected)
        throw new InvalidDataException($"A Second Nature slide starts at {offset} and the one before it ends at {expected}.");
      if (length <= SecondNatureSlideShowFile.SlideHeaderSize || (long)offset + length > data.Length)
        throw new InvalidDataException($"A Second Nature slide states {length} bytes at {offset}, which the file cannot hold.");

      var record = data.Slice(offset, SecondNatureSlideShowFile.SlideHeaderSize);
      var width = BinaryPrimitives.ReadUInt16LittleEndian(record[SecondNatureSlideShowFile.SlideSizeOffset..]);
      var height = BinaryPrimitives.ReadUInt16LittleEndian(record[(SecondNatureSlideShowFile.SlideSizeOffset + 2)..]);
      var againWidth = BinaryPrimitives.ReadUInt16LittleEndian(record[SecondNatureSlideShowFile.SlideSizeRepeatOffset..]);
      var againHeight = BinaryPrimitives.ReadUInt16LittleEndian(record[(SecondNatureSlideShowFile.SlideSizeRepeatOffset + 2)..]);
      if (width != againWidth || height != againHeight)
        throw new InvalidDataException($"A Second Nature slide states {width}x{height} in one place and {againWidth}x{againHeight} in another.");
      if (width < 1 || height < 1)
        throw new InvalidDataException("A Second Nature slide states no size.");

      var jpeg = data.Slice(offset + SecondNatureSlideShowFile.SlideHeaderSize, length - SecondNatureSlideShowFile.SlideHeaderSize);
      if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8 || jpeg[^2] != 0xFF || jpeg[^1] != 0xD9)
        throw new InvalidDataException($"A Second Nature slide at {offset} does not hold a JPEG.");

      slides.Add(new(width, height, jpeg.ToArray()));
      expected = offset + length;
    }

    if (expected != data.Length)
      throw new InvalidDataException($"A Second Nature collection's slides end at {expected} and the file is {data.Length} bytes.");

    return new() { Title = _Text(data.Slice(_TitleOffset, Math.Min(_TitleLength, data.Length - _TitleOffset))), Slides = slides };
  }

  public static SecondNatureSlideShowFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static string _Text(ReadOnlySpan<byte> field) {
    var stop = field.IndexOf((byte)0);
    return Encoding.ASCII.GetString(stop < 0 ? field : field[..stop]);
  }
}
