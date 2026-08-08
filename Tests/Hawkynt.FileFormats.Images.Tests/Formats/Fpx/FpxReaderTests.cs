using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Fpx;

namespace FileFormat.Fpx.Tests;

[TestFixture]
public sealed class FpxReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FpxReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FpxReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fpx"));
    Assert.Throws<FileNotFoundException>(() => FpxReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FpxReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => FpxReader.FromBytes(new byte[10]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TheOldInventedHeaderIsNotAFlashPixFile() {
    // "FPX\0", a version, a width and a height, then raw RGB. That is what this reader used to
    // accept and what the writer beside it used to produce, and no FlashPix file has ever looked
    // like it — a FlashPix picture is a compound file.
    var data = new byte[16 + 3];
    Encoding.ASCII.GetBytes("FPX\0").CopyTo(data, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 1);

    Assert.Throws<InvalidDataException>(() => FpxReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ACompoundFileWithoutImageContentsIsRefused() {
    // Renaming the stream leaves a compound file that is well formed and holds no picture, which is
    // what a spreadsheet or a presentation under the same signature is.
    var data = FpxFixture.Document();
    FpxFixture.Rename(data, "Image Contents", "Workbook      ");

    Assert.Throws<InvalidDataException>(() => FpxReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ATileCountThatIsNotTheGridIsRefused() {
    var data = FpxFixture.Document(tileCount: 2);

    var thrown = Assert.Throws<InvalidDataException>(() => FpxReader.FromBytes(data));
    Assert.That(thrown!.Message, Does.Contain("tiles"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ATileSideOtherThanSixtyFourIsRefused() {
    Assert.Throws<InvalidDataException>(() => FpxReader.FromBytes(FpxFixture.Document(tileSide: 128)));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ACompressionThatHasNotBeenCheckedIsRefused() {
    var thrown = Assert.Throws<InvalidDataException>(() => FpxReader.FromBytes(FpxFixture.Document(compression: 7)));
    Assert.That(thrown!.Message, Does.Contain("compression 7"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ASingleColourTileFillsTheSubimage() {
    // Luma at full with both chromas in the middle is white, which the conversion has to get right
    // in both directions to produce.
    var read = FpxReader.FromBytes(FpxFixture.Document());

    Assert.That(read.Width, Is.EqualTo(3));
    Assert.That(read.Height, Is.EqualTo(2));
    Assert.That(read.PixelData.Length, Is.EqualTo(3 * 2 * 3));
    Assert.That(read.PixelData[0], Is.EqualTo(255));
    Assert.That(read.PixelData[1], Is.EqualTo(255));
    Assert.That(read.PixelData[2], Is.EqualTo(255));
    Assert.That(read.PixelData[^1], Is.EqualTo(255));
  }
}

/// <summary>Builds the smallest compound file that is a FlashPix picture.</summary>
internal static class FpxFixture {

  private const int _SectorSize = 512;
  private const int _HeaderSize = 512;
  private const uint _EndOfChain = 0xFFFFFFFE;
  private const uint _FatSector = 0xFFFFFFFD;
  private const uint _Free = 0xFFFFFFFF;

  internal static byte[] Document(int tileCount = 1, int tileSide = 64, int compression = 1) {

    var contents = new byte[56];
    BinaryPrimitives.WriteUInt16LittleEndian(contents.AsSpan(0), 0xFFFE);
    BinaryPrimitives.WriteUInt32LittleEndian(contents.AsSpan(24), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(contents.AsSpan(44), 48);
    BinaryPrimitives.WriteUInt32LittleEndian(contents.AsSpan(48), 8);
    BinaryPrimitives.WriteUInt32LittleEndian(contents.AsSpan(52), 0);

    var header = new byte[64 + tileCount * 16];
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(32), 3);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(36), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40), (uint)tileCount);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(44), (uint)tileSide);
    for (var i = 0; i < tileCount; ++i) {
      BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(64 + i * 16), (uint)(i * 3));
      BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(68 + i * 16), 3);
      BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(72 + i * 16), (uint)compression);
      BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(76 + i * 16), 0);
    }

    var tiles = new byte[tileCount * 3];
    for (var i = 0; i < tileCount; ++i) {
      tiles[i * 3] = 255;
      tiles[i * 3 + 1] = 128;
      tiles[i * 3 + 2] = 128;
    }

    return _Build([
      ("Data Object Store 000001", 1, []),
      ("Image Contents", 2, contents),
      ("Resolution 0000", 1, []),
      ("Subimage 0000 Header", 2, header),
      ("Subimage 0000 Data", 2, tiles),
    ]);
  }

  /// <summary>Overwrites a directory entry's name, leaving the rest of the file as it was.</summary>
  internal static void Rename(byte[] document, string from, string to) {
    var wanted = Encoding.Unicode.GetBytes(from);
    for (var at = _HeaderSize + _SectorSize; at + wanted.Length <= document.Length; at += 128) {
      if (!document.AsSpan(at, wanted.Length).SequenceEqual(wanted))
        continue;

      Encoding.Unicode.GetBytes(to).CopyTo(document.AsSpan(at));
      return;
    }

    throw new InvalidOperationException($"No directory entry named {from}.");
  }

  private static byte[] _Build((string Name, byte Type, byte[] Data)[] entries) {

    // Sector 0 is the allocation table, sectors 1 and 2 the directory, and one sector each for the
    // streams — enough that the reader has to walk every part of the structure.
    const int directorySectors = 2;
    var streamSectors = 0;
    foreach (var entry in entries)
      if (entry.Type == 2)
        ++streamSectors;

    var sectors = 1 + directorySectors + streamSectors;
    var file = new byte[_HeaderSize + sectors * _SectorSize];

    ReadOnlySpan<byte> signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    signature.CopyTo(file);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(26), 3);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(28), 0xFFFE);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(30), 9);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(32), 6);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(44), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(48), 1);
    // A cutoff of nothing puts every stream in the ordinary allocation, so the fixture needs no
    // short-sector table.
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(56), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(60), _EndOfChain);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(64), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(68), _EndOfChain);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(72), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(76), 0);
    for (var i = 1; i < 109; ++i)
      BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(76 + i * 4), _Free);

    var fat = _HeaderSize;
    for (var i = 0; i < _SectorSize / 4; ++i)
      BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(fat + i * 4), _Free);

    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(fat), _FatSector);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(fat + 4), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(fat + 8), _EndOfChain);
    for (var i = 0; i < streamSectors; ++i)
      BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(fat + (3 + i) * 4), _EndOfChain);

    var directory = _HeaderSize + _SectorSize;
    _WriteEntry(file, directory, "Root Entry", 5, 0, 0, _Free, _Free, 1);

    var nextSector = 3u;
    for (var i = 0; i < entries.Length; ++i) {
      var (name, type, data) = entries[i];
      var at = directory + (i + 1) * 128;
      var start = _Free;

      if (type == 2) {
        start = nextSector;
        data.CopyTo(file.AsSpan(_HeaderSize + (int)nextSector * _SectorSize));
        ++nextSector;
      }

      // Everything under one parent is threaded on the right, which an in-order walk reads as a
      // list — a legal shape, and the one that keeps the fixture readable.
      var right = i + 1 < entries.Length && _ParentOf(entries, i) == _ParentOf(entries, i + 1)
        ? (uint)(i + 2)
        : _Free;
      var child = type == 1 ? (uint)(i + 2) : _Free;

      _WriteEntry(file, at, name, type, start, data.Length, _Free, right, child);
    }

    return file;
  }

  /// <summary>Which storage an entry sits under, by the order the fixture lists them in.</summary>
  private static int _ParentOf((string Name, byte Type, byte[] Data)[] entries, int index) {
    var parent = -1;
    for (var i = 0; i < index; ++i)
      if (entries[i].Type == 1)
        parent = i;

    return parent;
  }

  private static void _WriteEntry(
    byte[] file, int at, string name, byte type, uint start, long size, uint left, uint right, uint child) {

    var bytes = Encoding.Unicode.GetBytes(name);
    bytes.CopyTo(file.AsSpan(at));
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at + 64), (ushort)(bytes.Length + 2));
    file[at + 66] = type;
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 68), left);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 72), right);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 76), child);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 116), start);
    BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(at + 120), (ulong)size);
  }
}
