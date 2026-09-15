using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Cur;
using FileFormat.Ico;

namespace FileFormat.Ico.Tests;

/// <summary>
/// The one directory walk that reads an icon or a cursor without being told which.
/// </summary>
[TestFixture]
public sealed class IconBundleTests {

  private const int _HeaderSize = 6;
  private const int _EntrySize = 16;
  private const int _InfoHeaderSize = 40;

  /// <summary>An entry payload: a bitmap of the given size and depth, mask included.</summary>
  private static byte[] _IconDib(int width, int height, int bitsPerPixel, int paletteEntries = 0) {
    var colourBytes = (width * bitsPerPixel + 31) / 32 * 4 * height;
    var maskBytes = (width + 31) / 32 * 4 * height;
    var paletteBytes = paletteEntries * 4;
    var dib = new byte[_InfoHeaderSize + paletteBytes + colourBytes + maskBytes];

    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(0), _InfoHeaderSize);
    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), height * 2);
    BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), (ushort)bitsPerPixel);
    if (paletteEntries > 0)
      BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(32), paletteEntries);

    return dib;
  }

  private static byte[] _MinimalPng(int width, int height) {
    static byte[] Chunk(string type, byte[] body) {
      var chunk = new byte[4 + 4 + body.Length + 4];
      BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(0), (uint)body.Length);
      for (var i = 0; i < 4; ++i)
        chunk[4 + i] = (byte)type[i];
      body.CopyTo(chunk.AsSpan(8));
      return chunk;
    }

    var ihdr = new byte[13];
    BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)width);
    BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
    ihdr[8] = 8;
    ihdr[9] = 6;

    using var ms = new MemoryStream();
    ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    ms.Write(Chunk("IHDR", ihdr));
    ms.Write(Chunk("IEND", []));
    return ms.ToArray();
  }

  /// <summary>
  /// A file of the given type whose one entry carries the given payload, with the four directory
  /// bytes that differ between an icon and a cursor set to whatever the caller says.
  /// </summary>
  private static byte[] _OneEntryFile(
    IcoFileType kind,
    byte[] payload,
    byte widthByte,
    byte heightByte,
    ushort field4,
    ushort field5,
    byte colourCount = 0
  ) {
    var file = new byte[_HeaderSize + _EntrySize + payload.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0), 0);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), (ushort)kind);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4), 1);

    file[_HeaderSize + 0] = widthByte;
    file[_HeaderSize + 1] = heightByte;
    file[_HeaderSize + 2] = colourCount;
    file[_HeaderSize + 3] = 0;
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(_HeaderSize + 4), field4);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(_HeaderSize + 6), field5);
    BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(_HeaderSize + 8), payload.Length);
    BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(_HeaderSize + 12), _HeaderSize + _EntrySize);

    payload.CopyTo(file, _HeaderSize + _EntrySize);
    return file;
  }

  // ── Either type, reported rather than required ────────────────────────────

  [Test]
  [Category("Unit")]
  public void ReadBundle_ReportsAnIconAsAnIcon() {
    var file = _OneEntryFile(IcoFileType.Icon, _IconDib(16, 16, 32), 16, 16, 1, 32);

    var bundle = IcoReader.ReadBundle(file);

    Assert.That(bundle.Kind, Is.EqualTo(IcoFileType.Icon));
    Assert.That(bundle.Entries, Has.Count.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ReadBundle_ReportsACursorAsACursor() {
    var file = _OneEntryFile(IcoFileType.Cursor, _IconDib(16, 16, 32), 16, 16, 4, 5);

    var bundle = IcoReader.ReadBundle(file);

    Assert.That(bundle.Kind, Is.EqualTo(IcoFileType.Cursor));
    Assert.That(bundle.Entries, Has.Count.EqualTo(1));
  }

  // ── The two bytes an icon and a cursor disagree about ─────────────────────

  [Test]
  [Category("Boundary")]
  public void ReadBundle_ReadsACursorsHotspotRatherThanTakingItForADepth() {
    // A hotspot of (1, 24) is exactly what an icon would call one plane at 24 bits, which is why
    // reading the field without knowing the type produces a plausible wrong answer instead of an
    // obviously wrong one. The payload is 32-bit, so the depth is checkable.
    var file = _OneEntryFile(IcoFileType.Cursor, _IconDib(16, 16, 32), 16, 16, 1, 24);

    var entry = IcoReader.ReadBundle(file).Entries[0];

    Assert.That(entry.HotspotX, Is.EqualTo(1));
    Assert.That(entry.HotspotY, Is.EqualTo(24));
    Assert.That(entry.BitsPerPixel, Is.EqualTo(32), "the depth comes from the payload, not the hotspot");
  }

  [Test]
  [Category("Boundary")]
  public void ReadBundle_AnIconHasNoHotspotAndItsFieldsAreThePlaneAndDepth() {
    var file = _OneEntryFile(IcoFileType.Icon, _IconDib(16, 16, 8, paletteEntries: 256), 16, 16, 1, 8);

    var entry = IcoReader.ReadBundle(file).Entries[0];

    Assert.That(entry.HotspotX, Is.EqualTo(0));
    Assert.That(entry.HotspotY, Is.EqualTo(0));
    Assert.That(entry.BitsPerPixel, Is.EqualTo(8));
  }

  [Test]
  [Category("Boundary")]
  public void CurReader_ReadsTheHotspotAndTheDepthTogether() {
    // The same claim through the public cursor reader: it used to parse the file twice and the
    // first pass read the hotspot's lower half as the depth.
    var file = _OneEntryFile(IcoFileType.Cursor, _IconDib(24, 24, 32), 24, 24, 7, 9);

    var cursor = CurReader.FromBytes(file);

    Assert.That(cursor.Images[0].HotspotX, Is.EqualTo(7));
    Assert.That(cursor.Images[0].HotspotY, Is.EqualTo(9));
    Assert.That(cursor.Images[0].BitsPerPixel, Is.EqualTo(32));
  }

  // ── Sizes: the directory byte, nought meaning 256, and the payload ────────

  [Test]
  [Category("Boundary")]
  public void ReadBundle_ZeroSideWithABitmapThatStatesARealSizeTakesThePayloadsWord() {
    // One cursor in the corpus states 0 by 0 and is a 32 by 32 arrow, which every other viewer
    // draws at 32. Nought is only 256 when nothing better is available.
    var file = _OneEntryFile(IcoFileType.Cursor, _IconDib(32, 32, 32), 0, 0, 0, 0);

    var entry = IcoReader.ReadBundle(file).Entries[0];

    Assert.That(entry.Width, Is.EqualTo(32));
    Assert.That(entry.Height, Is.EqualTo(32));
  }

  [Test]
  [Category("Boundary")]
  public void ReadBundle_ZeroSideWithAPngMeansTwoFiftySixWhenThePngSaysSo() {
    var file = _OneEntryFile(IcoFileType.Icon, _MinimalPng(256, 256), 0, 0, 1, 32);

    var entry = IcoReader.ReadBundle(file).Entries[0];

    Assert.That(entry.Width, Is.EqualTo(256));
    Assert.That(entry.Height, Is.EqualTo(256));
    Assert.That(entry.Format, Is.EqualTo(IcoImageFormat.Png));
  }

  [Test]
  [Category("Boundary")]
  public void ReadBundle_HalvesTheBitmapsStatedHeightBecauseItCoversTheMask() {
    var file = _OneEntryFile(IcoFileType.Icon, _IconDib(20, 10, 32), 20, 10, 1, 32);
    Assert.That(BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(_HeaderSize + _EntrySize + 8)), Is.EqualTo(20));

    var entry = IcoReader.ReadBundle(file).Entries[0];

    Assert.That(entry.Width, Is.EqualTo(20));
    Assert.That(entry.Height, Is.EqualTo(10));
  }

  // ── Depths ───────────────────────────────────────────────────────────────

  [TestCase(1, 2)]
  [TestCase(4, 16)]
  [TestCase(8, 256)]
  [TestCase(24, 0)]
  [TestCase(32, 0)]
  [Category("Boundary")]
  public void ReadBundle_TakesTheDepthFromTheBitmapAtEveryDepthAnEntryCanHold(int bitsPerPixel, int paletteEntries) {
    var file = _OneEntryFile(IcoFileType.Icon, _IconDib(16, 16, bitsPerPixel, paletteEntries), 16, 16, 1, 32);

    var entry = IcoReader.ReadBundle(file).Entries[0];

    Assert.That(entry.BitsPerPixel, Is.EqualTo(bitsPerPixel));
  }

  [Test]
  [Category("Boundary")]
  public void ReadBundle_DirectoryDepthDisagreeingWithTheBitmapLosesToTheBitmap() {
    // The directory says 8 and the bitmap says 32. The bitmap is what the colours actually are; a
    // directory entry is a summary written by whoever assembled the file.
    var file = _OneEntryFile(IcoFileType.Icon, _IconDib(16, 16, 32), 16, 16, 1, 8, colourCount: 16);

    var entry = IcoReader.ReadBundle(file).Entries[0];

    Assert.That(entry.BitsPerPixel, Is.EqualTo(32));
  }

  [Test]
  [Category("Boundary")]
  public void ReadBundle_BitmapStatingNoDepthFallsBackToTheDirectorys() {
    var dib = _IconDib(16, 16, 32);
    BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 0);
    var file = _OneEntryFile(IcoFileType.Icon, dib, 16, 16, 1, 24);

    var entry = IcoReader.ReadBundle(file).Entries[0];

    Assert.That(entry.BitsPerPixel, Is.EqualTo(24));
  }

  // ── PNG-bodied and bitmap-bodied entries side by side ─────────────────────

  [Test]
  [Category("Integration")]
  public void ReadBundle_DecidesPerEntryWhetherThePayloadIsAPngOrABitmap() {
    var png = _MinimalPng(32, 32);
    var dib = _IconDib(16, 16, 32);

    var entries = new IconBundleEntry[] {
      new(0, 32, 32, 32, 0, 0, IcoImageFormat.Png, png),
      new(1, 16, 16, 32, 0, 0, IcoImageFormat.Bmp, dib),
    };
    var file = IcoWriter.Assemble(IcoFileType.Icon, entries);

    var bundle = IcoReader.ReadBundle(file);

    Assert.That(bundle.Entries, Has.Count.EqualTo(2));
    Assert.That(bundle.Entries[0].Format, Is.EqualTo(IcoImageFormat.Png));
    Assert.That(bundle.Entries[1].Format, Is.EqualTo(IcoImageFormat.Bmp));
    Assert.That(bundle.Entries[0].Data, Is.EqualTo(png).AsCollection);
    Assert.That(bundle.Entries[1].Data, Is.EqualTo(dib).AsCollection);
  }

  // ── Writing back what was read ───────────────────────────────────────────

  [Test]
  [Category("Integration")]
  public void Assemble_IsTheInverseOfReadBundleForACursorIncludingItsHotspots() {
    var original = new IconBundleEntry[] {
      new(0, 16, 16, 32, 3, 4, IcoImageFormat.Bmp, _IconDib(16, 16, 32)),
      new(1, 32, 32, 32, 15, 16, IcoImageFormat.Bmp, _IconDib(32, 32, 32)),
    };

    var bytes = IcoWriter.Assemble(IcoFileType.Cursor, original);
    var bundle = IcoReader.ReadBundle(bytes);

    Assert.That(bundle.Kind, Is.EqualTo(IcoFileType.Cursor));
    Assert.That(bundle.Entries, Has.Count.EqualTo(2));
    for (var i = 0; i < original.Length; ++i) {
      Assert.That(bundle.Entries[i].Width, Is.EqualTo(original[i].Width));
      Assert.That(bundle.Entries[i].Height, Is.EqualTo(original[i].Height));
      Assert.That(bundle.Entries[i].BitsPerPixel, Is.EqualTo(original[i].BitsPerPixel));
      Assert.That(bundle.Entries[i].HotspotX, Is.EqualTo(original[i].HotspotX));
      Assert.That(bundle.Entries[i].HotspotY, Is.EqualTo(original[i].HotspotY));
      Assert.That(bundle.Entries[i].Data, Is.EqualTo(original[i].Data).AsCollection);
    }
  }

  [TestCase(1, 2)]
  [TestCase(4, 16)]
  [TestCase(8, 0)]
  [TestCase(32, 0)]
  [Category("Unit")]
  public void Assemble_StatesThePaletteSizeADepthImplies(int bitsPerPixel, int expectedColourCount) {
    // The field is one byte, so a 256-colour palette and no palette at all are both nought.
    var entries = new IconBundleEntry[] { new(0, 16, 16, bitsPerPixel, 0, 0, IcoImageFormat.Bmp, _IconDib(16, 16, bitsPerPixel)) };

    var bytes = IcoWriter.Assemble(IcoFileType.Icon, entries);

    Assert.That(bytes[_HeaderSize + 2], Is.EqualTo((byte)expectedColourCount));
  }

  [Test]
  [Category("Boundary")]
  public void Assemble_WritesTwoFiftySixAsNought() {
    var entries = new IconBundleEntry[] { new(0, 256, 256, 32, 0, 0, IcoImageFormat.Png, _MinimalPng(256, 256)) };

    var bytes = IcoWriter.Assemble(IcoFileType.Icon, entries);

    Assert.That(bytes[_HeaderSize + 0], Is.EqualTo(0));
    Assert.That(bytes[_HeaderSize + 1], Is.EqualTo(0));
  }

  // ── Malformed input ──────────────────────────────────────────────────────

  [Test]
  [Category("Exceptional")]
  public void ReadBundle_TooShortForAHeader_Throws() {
    Assert.Throws<InvalidDataException>(() => IcoReader.ReadBundle(new byte[] { 0, 0, 1 }));
  }

  [Test]
  [Category("Exceptional")]
  public void ReadBundle_ReservedFieldNotNought_Throws() {
    var file = _OneEntryFile(IcoFileType.Icon, _IconDib(8, 8, 32), 8, 8, 1, 32);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0), 1);

    Assert.Throws<InvalidDataException>(() => IcoReader.ReadBundle(file));
  }

  [Test]
  [Category("Exceptional")]
  public void ReadBundle_NeitherIconNorCursor_Throws() {
    var file = _OneEntryFile(IcoFileType.Icon, _IconDib(8, 8, 32), 8, 8, 1, 32);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), 99);

    Assert.Throws<InvalidDataException>(() => IcoReader.ReadBundle(file));
  }

  [Test]
  [Category("Exceptional")]
  public void ReadBundle_DirectoryRunsPastTheEndOfTheFile_Throws() {
    var file = new byte[_HeaderSize + _EntrySize];
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), (ushort)IcoFileType.Icon);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4), 4);  // four entries, room for one

    Assert.Throws<InvalidDataException>(() => IcoReader.ReadBundle(file));
  }

  [Test]
  [Category("Exceptional")]
  public void ReadBundle_EntryPayloadRunsPastTheEndOfTheFile_Throws() {
    var file = _OneEntryFile(IcoFileType.Icon, _IconDib(8, 8, 32), 8, 8, 1, 32);
    BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(_HeaderSize + 8), file.Length);

    Assert.Throws<InvalidDataException>(() => IcoReader.ReadBundle(file));
  }

  [Test]
  [Category("Exceptional")]
  public void ReadBundle_NegativeSizeOrOffset_Throws() {
    var file = _OneEntryFile(IcoFileType.Icon, _IconDib(8, 8, 32), 8, 8, 1, 32);
    BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(_HeaderSize + 12), -1);

    // A size or offset read as unsigned wraps on addition and slips past a bounds check, so the
    // sign is rejected before the arithmetic rather than after it.
    Assert.Throws<InvalidDataException>(() => IcoReader.ReadBundle(file));
  }

  [Test]
  [Category("Boundary")]
  public void ReadBundle_OverlappingEntriesAreBothRead() {
    // Two entries pointing at the same payload is not forbidden and is how a file shares one
    // picture between two sizes. Nothing here deduplicates or objects.
    var dib = _IconDib(16, 16, 32);
    var file = new byte[_HeaderSize + 2 * _EntrySize + dib.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), (ushort)IcoFileType.Icon);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4), 2);

    var payloadAt = _HeaderSize + 2 * _EntrySize;
    for (var i = 0; i < 2; ++i) {
      var at = _HeaderSize + i * _EntrySize;
      file[at] = 16;
      file[at + 1] = 16;
      BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at + 4), 1);
      BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at + 6), 32);
      BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(at + 8), dib.Length);
      BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(at + 12), payloadAt);
    }
    dib.CopyTo(file, payloadAt);

    var bundle = IcoReader.ReadBundle(file);

    Assert.That(bundle.Entries, Has.Count.EqualTo(2));
    Assert.That(bundle.Entries[0].Data, Is.EqualTo(bundle.Entries[1].Data).AsCollection);
  }

  [Test]
  [Category("Exceptional")]
  public void ReadBundle_NullArray_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IcoReader.ReadBundle((byte[])null!));
  }

  [Test]
  [Category("Exceptional")]
  public void FromSpan_HandedACursor_StillRefusesItBecauseItAskedForAnIcon() {
    var file = _OneEntryFile(IcoFileType.Cursor, _IconDib(8, 8, 32), 8, 8, 0, 0);

    Assert.Throws<InvalidDataException>(() => IcoReader.FromBytes(file));
  }

  [Test]
  [Category("Exceptional")]
  public void CurReader_HandedAnIcon_Refuses() {
    var file = _OneEntryFile(IcoFileType.Icon, _IconDib(8, 8, 32), 8, 8, 1, 32);

    Assert.Throws<InvalidDataException>(() => CurReader.FromBytes(file));
  }
}
