using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.PaintShopBrowser;

/// <summary>Writes a Paint Shop Pro browser cache in the version 2 layout.</summary>
/// <remarks>
/// Version 2, where a thumbnail is a whole JPEG with its own length in front of it. Version 1 stores
/// a run-length coding against a palette that is not in the file, whose two variants are told apart
/// by the minor version, and no file of it was ever available here — the reader refuses one by name
/// and the writer does not produce one.
/// <para/>
/// One record follows another with nothing between them, so every field a record states is what puts
/// the next record in the right place: the name behind its own length, the six words after the file
/// time, the two nobody has identified, the four set bytes that say a thumbnail follows, and then the
/// thumbnail's length. All of them are written from what actually follows.
/// <para/>
/// The two version numbers go out most significant byte first and everything else the other way
/// round, which is the one thing about this format that catches people out.
/// </remarks>
public static class PaintShopBrowserWriter {

  /// <summary>Where the version sits, where the count after it does, and where the folder does.</summary>
  private const int _VersionOffset = 15, _CountOffset = 19, _DirectoryOffset = 23;

  /// <summary>What is written where the reader looks for the four set bytes' two companions.</summary>
  private const int _UnknownWords = 8;

  /// <summary>The file time and the six words after it.</summary>
  private const int _RecordFields = 8 + 6 * 4;

  public static byte[] ToBytes(PaintShopBrowserFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Thumbnails.Count == 0)
      throw new ArgumentException("A browser cache records thumbnails and this one has none.", nameof(file));

    var (major, minor) = file.Version;
    if (major == 0)
      major = 2;

    if (major != 2)
      throw new ArgumentException(
        $"Only the version 2 caches are written, whose thumbnails are JPEGs; a version {major}.{minor} one codes its thumbnails against a palette that is not in the file.", nameof(file));

    var header = new byte[PaintShopBrowserFile.HeaderLength];
    Encoding.ASCII.GetBytes(PaintShopBrowserFile.Magic).CopyTo(header, 0);
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(_VersionOffset), (ushort)major);
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(_VersionOffset + 2), (ushort)minor);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(_CountOffset), (uint)file.Thumbnails.Count);

    var directory = Encoding.Latin1.GetBytes(file.Directory ?? string.Empty);
    Array.Copy(directory, 0, header, _DirectoryOffset, Math.Min(directory.Length, PaintShopBrowserFile.HeaderLength - _DirectoryOffset - 1));

    using var output = new MemoryStream();
    output.Write(header);

    foreach (var thumbnail in file.Thumbnails) {
      var jpeg = thumbnail.Jpeg;
      if (jpeg == null || jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        throw new ArgumentException("A cached thumbnail is a whole JPEG file and this one does not begin as one.", nameof(file));

      var name = Encoding.Latin1.GetBytes(thumbnail.Name ?? string.Empty);
      _UInt32(output, name.Length);
      output.Write(name);

      var fields = new byte[_RecordFields];
      BinaryPrimitives.WriteUInt32LittleEndian(fields.AsSpan(12), (uint)Math.Max(0, thumbnail.Width));
      BinaryPrimitives.WriteUInt32LittleEndian(fields.AsSpan(16), (uint)Math.Max(0, thumbnail.Height));
      output.Write(fields);

      output.Write(new byte[_UnknownWords]);
      _UInt32(output, unchecked((int)PaintShopBrowserFile.ThumbnailSentinel));
      _UInt32(output, jpeg.Length);
      output.Write(jpeg);
    }

    return output.ToArray();
  }

  private static void _UInt32(Stream output, int value) {
    Span<byte> word = stackalloc byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(word, value);
    output.Write(word);
  }
}
