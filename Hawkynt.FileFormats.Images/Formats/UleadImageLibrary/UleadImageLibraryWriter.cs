using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Jpeg;

namespace FileFormat.UleadImageLibrary;

/// <summary>Writes a Ulead image library: the header, then one record per item.</summary>
/// <remarks>
/// There is no directory in one of these and none is written. The chain is computed instead, exactly
/// as it is read: the count stands at 0x100, the first record begins at <c>0x210 + 4n</c>, and each
/// record states the length of its JPEG, of its metadata and of the extra block after it, which
/// together give where the next one starts.
/// <para/>
/// The size a record states is written from the picture, because the reader refuses a record whose
/// size is not the one the JPEG states of itself — that agreement is what says the walk is where the
/// format means it to be rather than somewhere a length happened to bracket a picture.
/// <para/>
/// The extra block is empty. In a real library it holds the full-size artwork in a form nothing here
/// has worked out, and the picture is not made worse by its absence: the record is the thumbnail's
/// record, and that is what is being written.
/// </remarks>
public static class UleadImageLibraryWriter {

  public static byte[] ToBytes(UleadImageLibraryFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Items.Count == 0)
      throw new ArgumentException("A Ulead image library holds items and this one has none.", nameof(file));

    if (file.Items.Count > UleadImageLibraryFile.MaximumItems)
      throw new ArgumentException($"A Ulead image library of {file.Items.Count} items is more than the {UleadImageLibraryFile.MaximumItems} one holds.", nameof(file));

    var header = new byte[UleadImageLibraryFile.FirstRecordBase + 4 * file.Items.Count];
    UleadImageLibraryFile.Magic.CopyTo(header);
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(UleadImageLibraryFile.ItemCountAt), file.Items.Count);

    using var output = new MemoryStream();
    output.Write(header);

    foreach (var jpeg in file.Items) {
      if (jpeg == null || jpeg.Length < 5 || jpeg[0] != 0xFF || jpeg[1] != 0xD8 || jpeg[^2] != 0xFF || jpeg[^1] != 0xD9)
        throw new ArgumentException("A Ulead image library item holds a whole JPEG and this one does not begin and end as one.", nameof(file));

      var decoded = JpegReader.FromBytes(jpeg);
      var record = new byte[UleadImageLibraryFile.RecordHeaderSize];
      BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(UleadImageLibraryFile.RecordTypeAt), UleadImageLibraryFile.RecordType);
      BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(UleadImageLibraryFile.ExtraLengthAt), 0);
      BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(UleadImageLibraryFile.WidthAt), decoded.Width);
      BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(UleadImageLibraryFile.HeightAt), decoded.Height);
      BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(UleadImageLibraryFile.PlaneCountAt), 3);
      BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(UleadImageLibraryFile.JpegLengthAt), jpeg.Length);

      output.Write(record);
      output.Write(jpeg);
      output.Write(new byte[UleadImageLibraryFile.MetadataSize]);
    }

    return output.ToArray();
  }
}
