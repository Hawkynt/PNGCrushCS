using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Jpeg;

namespace FileFormat.UleadAlbumTemplate;

/// <summary>Writes a Ulead album template set: the header, the records, then the directory.</summary>
/// <remarks>
/// Unlike the older library these carry a real directory, and it goes at the end because the header
/// states where it begins and how long it is and the two have to add up to the length of the file to
/// the byte. That is the reader's check that the header is being read as the format means it, so it
/// is what the offsets are written to.
/// <para/>
/// Each entry is where its record is and where its name is within the directory, and each record
/// states the size the JPEG after it states of itself — a record and a picture disagreeing is what
/// the reader refuses.
/// </remarks>
public static class UleadAlbumTemplateWriter {

  public static byte[] ToBytes(UleadAlbumTemplateFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Templates.Count == 0)
      throw new ArgumentException("A Ulead album template set holds templates and this one has none.", nameof(file));

    if (file.Templates.Count > UleadAlbumTemplateFile.MaximumEntries)
      throw new ArgumentException($"A Ulead album template set of {file.Templates.Count} templates is more than the {UleadAlbumTemplateFile.MaximumEntries} one holds.", nameof(file));

    var header = new byte[UleadAlbumTemplateFile.EntryCountAt + 4];
    UleadAlbumTemplateFile.Magic.CopyTo(header);
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(UleadAlbumTemplateFile.RecordHeaderSizeAt), UleadAlbumTemplateFile.DefaultRecordHeaderSize);
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(UleadAlbumTemplateFile.EntryCountAt), file.Templates.Count);

    using var body = new MemoryStream();
    var recordOffsets = new List<int>(file.Templates.Count);

    foreach (var template in file.Templates) {
      var jpeg = template.Embedded;
      if (jpeg == null || jpeg.Length < 5 || jpeg[0] != 0xFF || jpeg[1] != 0xD8 || jpeg[^2] != 0xFF || jpeg[^1] != 0xD9)
        throw new ArgumentException("A Ulead album template holds a whole JPEG and this one does not begin and end as one.", nameof(file));

      var decoded = JpegReader.FromBytes(jpeg);
      if (decoded.Width is < 1 or > ushort.MaxValue || decoded.Height is < 1 or > ushort.MaxValue)
        throw new ArgumentException(
          $"A Ulead album template states its size in unsigned words and {decoded.Width} by {decoded.Height} does not fit in them.", nameof(file));

      recordOffsets.Add(header.Length + (int)body.Length);

      var record = new byte[UleadAlbumTemplateFile.DefaultRecordHeaderSize];
      BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(UleadAlbumTemplateFile.WidthAt), (ushort)decoded.Width);
      BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(UleadAlbumTemplateFile.HeightAt), (ushort)decoded.Height);
      BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(UleadAlbumTemplateFile.PlaneCountAt), 3);
      BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(UleadAlbumTemplateFile.JpegLengthAt), jpeg.Length);
      body.Write(record);
      body.Write(jpeg);
    }

    // The names sit after the entries, inside the directory, and an entry points at one by its offset
    // from the directory's own start.
    var entriesLength = file.Templates.Count * UleadAlbumTemplateFile.DirectoryEntrySize;
    using var names = new MemoryStream();
    var nameOffsets = new List<int>(file.Templates.Count);
    foreach (var template in file.Templates) {
      nameOffsets.Add(entriesLength + (int)names.Length);
      names.Write(Encoding.ASCII.GetBytes(template.Name ?? string.Empty));
      names.WriteByte(0);
    }

    var directoryOffset = header.Length + (int)body.Length;

    using var output = new MemoryStream();
    output.Write(header);
    output.Write(body.ToArray());

    for (var i = 0; i < file.Templates.Count; ++i) {
      Span<byte> entry = stackalloc byte[UleadAlbumTemplateFile.DirectoryEntrySize];
      BinaryPrimitives.WriteInt32LittleEndian(entry, recordOffsets[i]);
      BinaryPrimitives.WriteInt32LittleEndian(entry[4..], nameOffsets[i]);
      output.Write(entry);
    }

    output.Write(names.ToArray());

    var result = output.ToArray();
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(UleadAlbumTemplateFile.DirectoryOffsetAt), directoryOffset);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(UleadAlbumTemplateFile.DirectoryLengthAt), result.Length - directoryOffset);

    return result;
  }
}
