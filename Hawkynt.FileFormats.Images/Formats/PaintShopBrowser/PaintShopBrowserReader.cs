using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.PaintShopBrowser;

/// <summary>Reads a Paint Shop Pro browser cache and pulls the thumbnails out of it.</summary>
/// <remarks>
/// One record follows another with no padding, so a record read wrongly puts every record after it
/// wrong too. That is why so much of this is checking: the header states how many thumbnails there
/// are and that many have to be there; each name states its own length and it has to fit; each
/// thumbnail is preceded by a sentinel of four set bytes and then a length, and the JPEG at that
/// length has to start with the two bytes a JPEG starts with. A record that fails any of those has
/// been misread, and carrying on would read a picture out of the middle of another one.
/// </remarks>
public static class PaintShopBrowserReader {

  /// <summary>How many thumbnails a cache may hold.</summary>
  private const int _MaxThumbnails = 1 << 16;

  /// <summary>How long a file name in a record may be.</summary>
  private const int _MaxNameLength = 1 << 12;

  /// <summary>How large one cached thumbnail may be.</summary>
  private const int _MaxThumbnailLength = 1 << 22;

  /// <summary>Where the version sits, and where the count after it does.</summary>
  private const int _VersionOffset = 15, _CountOffset = 19, _DirectoryOffset = 23;

  public static PaintShopBrowserFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Browser cache not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PaintShopBrowserFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static PaintShopBrowserFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static PaintShopBrowserFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < PaintShopBrowserFile.HeaderLength)
      throw new InvalidDataException($"A browser cache's header is {PaintShopBrowserFile.HeaderLength} bytes and this file is {data.Length}.");

    if (!data[..PaintShopBrowserFile.Magic.Length].SequenceEqual(Encoding.ASCII.GetBytes(PaintShopBrowserFile.Magic)))
      throw new InvalidDataException("Not a Paint Shop Pro browser cache: it does not open with \"JASC BROWS FILE\".");

    // The two version numbers are the only things in the file written most significant byte first.
    var major = BinaryPrimitives.ReadUInt16BigEndian(data[_VersionOffset..]);
    var minor = BinaryPrimitives.ReadUInt16BigEndian(data[(_VersionOffset + 2)..]);
    if (major == 1)
      throw new InvalidDataException($"This is a version {major}.{minor} browser cache, whose thumbnails are a bitmap coding against a palette that is not in the file; only the version 2 caches, whose thumbnails are JPEGs, are read here.");

    if (major != 2)
      throw new InvalidDataException($"A browser cache of version {major}.{minor} is not one either of the two known versions of this format.");

    var stated = BinaryPrimitives.ReadUInt32LittleEndian(data[_CountOffset..]);
    if (stated > _MaxThumbnails)
      throw new InvalidDataException($"A browser cache of {stated} thumbnails is more than a folder holds.");

    var directory = _Text(data[_DirectoryOffset..PaintShopBrowserFile.HeaderLength]);
    var thumbnails = new List<PaintShopThumbnail>((int)stated);
    var at = PaintShopBrowserFile.HeaderLength;

    for (var i = 0; i < stated; ++i)
      at = _Record(data, at, i, thumbnails);

    // The header says how many records follow it. A file with fewer has been cut, and one that is
    // simply empty is a cache of a folder with no pictures in it, which is not a picture either.
    if (thumbnails.Count == 0)
      throw new InvalidDataException("This browser cache records no thumbnails at all.");

    return new() { Version = (major, minor), Directory = directory, Thumbnails = thumbnails };
  }

  /// <summary>Reads one record and returns where the next one starts.</summary>
  private static int _Record(ReadOnlySpan<byte> data, int at, int index, List<PaintShopThumbnail> into) {
    var name = _Name(data, ref at, index);

    // Eight bytes of file time, then the original's kind, its width, its height, its depth, the
    // size a decompressed thumbnail would take, and the original file's size: six fields of four
    // bytes each after the time, all least significant byte first.
    _Need(data, at, 8 + 6 * 4, index);
    var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 12)..]);
    var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 16)..]);
    at += 8 + 6 * 4;

    // Two words nobody has identified, then four set bytes that say a thumbnail follows.
    _Need(data, at, 12, index);
    var sentinel = BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 8)..]);
    if (sentinel != PaintShopBrowserFile.ThumbnailSentinel)
      throw new InvalidDataException($"Record {index} has 0x{sentinel:X8} where a thumbnail's four set bytes should be, so the records have been read out of step.");

    at += 12;
    _Need(data, at, 4, index);
    var length = BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
    at += 4;

    if (length is < 4 or > _MaxThumbnailLength)
      throw new InvalidDataException($"Record {index} states a thumbnail of {length} bytes, which is not a JPEG file.");

    _Need(data, at, (int)length, index);
    var jpeg = data.Slice(at, (int)length);

    // The payload is a whole JPEG file, so it starts the way one does. Anything else means the
    // length was read from the wrong place.
    if (jpeg[0] != 0xFF || jpeg[1] != 0xD8)
      throw new InvalidDataException($"Record {index}'s thumbnail does not begin as a JPEG does, so its length was read from the wrong place.");

    into.Add(new(name, width, height, jpeg.ToArray()));

    return at + (int)length;
  }

  private static string _Name(ReadOnlySpan<byte> data, ref int at, int index) {
    _Need(data, at, 4, index);
    var length = BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
    at += 4;

    if (length > _MaxNameLength)
      throw new InvalidDataException($"Record {index} states a name of {length} bytes, which is longer than a path.");

    _Need(data, at, (int)length, index);
    var name = Encoding.Latin1.GetString(data.Slice(at, (int)length));
    at += (int)length;

    return name;
  }

  private static void _Need(ReadOnlySpan<byte> data, int at, int length, int index) {
    if (at < 0 || length < 0 || at + length > data.Length)
      throw new InvalidDataException($"Record {index} needs {length} bytes at {at} and the file ends at {data.Length}.");
  }

  /// <summary>The nul-terminated text at the start of a run of bytes.</summary>
  private static string _Text(ReadOnlySpan<byte> data) {
    var end = data.IndexOf((byte)0);

    return Encoding.Latin1.GetString(end < 0 ? data : data[..end]);
  }
}
