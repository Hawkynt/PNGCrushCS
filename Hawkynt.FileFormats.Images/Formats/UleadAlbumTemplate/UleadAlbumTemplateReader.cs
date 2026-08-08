using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Jpeg;

namespace FileFormat.UleadAlbumTemplate;

/// <summary>Reads Ulead album template sets from bytes, streams, or file paths.</summary>
public static class UleadAlbumTemplateReader {

  private static ReadOnlySpan<byte> JpegStart => [0xFF, 0xD8, 0xFF];
  private static ReadOnlySpan<byte> JpegEnd => [0xFF, 0xD9];

  public static UleadAlbumTemplateFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Ulead album template set not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static UleadAlbumTemplateFile FromStream(Stream stream) {
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

  public static UleadAlbumTemplateFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < UleadAlbumTemplateFile.EntryCountAt + 4)
      throw new InvalidDataException($"Data too small for a Ulead album template set (got {data.Length} bytes).");

    if (!data[..UleadAlbumTemplateFile.Magic.Length].SequenceEqual(UleadAlbumTemplateFile.Magic))
      throw new InvalidDataException("Not a Ulead album template set: it does not open the way one does.");

    var directoryOffset = BinaryPrimitives.ReadInt32LittleEndian(data[UleadAlbumTemplateFile.DirectoryOffsetAt..]);
    var directoryLength = BinaryPrimitives.ReadInt32LittleEndian(data[UleadAlbumTemplateFile.DirectoryLengthAt..]);
    var recordHeaderSize = BinaryPrimitives.ReadInt32LittleEndian(data[UleadAlbumTemplateFile.RecordHeaderSizeAt..]);
    var count = BinaryPrimitives.ReadInt32LittleEndian(data[UleadAlbumTemplateFile.EntryCountAt..]);

    // The directory runs to the end of the file, which is what says the header is being read as the
    // format means it rather than somewhere that happens to hold two plausible numbers.
    if (directoryOffset < 0 || directoryLength < 0 || directoryOffset + (long)directoryLength != data.Length)
      throw new InvalidDataException(
        $"A Ulead album template set states a directory of {directoryLength} bytes at {directoryOffset}, which does not account for a file of {data.Length}.");

    if (count < 1 || count > UleadAlbumTemplateFile.MaximumEntries
        || (long)count * UleadAlbumTemplateFile.DirectoryEntrySize > directoryLength)
      throw new InvalidDataException($"A Ulead album template set states {count} templates in {directoryLength} bytes of directory.");

    if (recordHeaderSize < UleadAlbumTemplateFile.JpegLengthAt + 8)
      throw new InvalidDataException($"A Ulead album template set states a record header of {recordHeaderSize} bytes.");

    var templates = new List<UleadAlbumTemplateFile.Template>(count);

    for (var i = 0; i < count; ++i) {
      var entry = directoryOffset + i * UleadAlbumTemplateFile.DirectoryEntrySize;
      var recordOffset = BinaryPrimitives.ReadInt32LittleEndian(data[entry..]);
      var nameOffset = BinaryPrimitives.ReadInt32LittleEndian(data[(entry + 4)..]);

      if (recordOffset < 0 || recordOffset + (long)recordHeaderSize > data.Length)
        throw new InvalidDataException($"Template {i} of a Ulead album template set points at {recordOffset} in a file of {data.Length}.");

      var width = BinaryPrimitives.ReadUInt16LittleEndian(data[(recordOffset + UleadAlbumTemplateFile.WidthAt)..]);
      var height = BinaryPrimitives.ReadUInt16LittleEndian(data[(recordOffset + UleadAlbumTemplateFile.HeightAt)..]);
      var jpegLength = BinaryPrimitives.ReadInt32LittleEndian(data[(recordOffset + UleadAlbumTemplateFile.JpegLengthAt)..]);

      if (width < 1 || height < 1 || jpegLength < JpegStart.Length + JpegEnd.Length)
        throw new InvalidDataException($"Template {i} of a Ulead album template set states {width}x{height} in {jpegLength} bytes.");

      var start = recordOffset + recordHeaderSize;
      if (start + (long)jpegLength > data.Length)
        throw new InvalidDataException($"Template {i} of a Ulead album template set states {jpegLength} bytes of picture and the file has {data.Length - start}.");

      var jpeg = data.Slice(start, jpegLength);
      if (!jpeg[..JpegStart.Length].SequenceEqual(JpegStart) || !jpeg[^JpegEnd.Length..].SequenceEqual(JpegEnd))
        throw new InvalidDataException($"Template {i} of a Ulead album template set states {jpegLength} bytes that do not begin and end a JPEG.");

      var embedded = jpeg.ToArray();

      var decoded = JpegReader.FromBytes(embedded);
      if (decoded.Width != width || decoded.Height != height)
        throw new InvalidDataException(
          $"Template {i} of a Ulead album template set says {width}x{height} and the JPEG it carries is {decoded.Width}x{decoded.Height}.");

      templates.Add(new(_ReadName(data, directoryOffset + nameOffset), embedded));
    }

    return new() { Templates = templates };
  }

  /// <summary>The template's name, which the directory keeps null-terminated after its entries.</summary>
  private static string _ReadName(ReadOnlySpan<byte> data, int at) {
    if (at < 0 || at >= data.Length)
      return string.Empty;

    var rest = data[at..];
    var end = rest.IndexOf((byte)0);
    return Encoding.ASCII.GetString(end < 0 ? rest : rest[..end]);
  }

  public static UleadAlbumTemplateFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
