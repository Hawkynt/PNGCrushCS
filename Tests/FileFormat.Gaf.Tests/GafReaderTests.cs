using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Gaf;

namespace FileFormat.Gaf.Tests;

[TestFixture]
public sealed class GafReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GafReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GafReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gaf"));
    Assert.Throws<FileNotFoundException>(() => GafReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GafReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => GafReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[100];
    BinaryPrimitives.WriteUInt32LittleEndian(data, 0xDEADBEEF);
    Assert.Throws<InvalidDataException>(() => GafReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidUncompressed_ParsesCorrectly() {
    var data = BuildMinimalGaf("tank_body", 4, 3, 9, false);
    var result = GafReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.Name, Is.EqualTo("tank_body"));
    Assert.That(result.TransparencyIndex, Is.EqualTo(9));
    Assert.That(result.PixelData.Length, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidUncompressed_PixelDataPreserved() {
    var data = BuildMinimalGaf("sprite", 2, 2, 9, false);
    var result = GafReader.FromBytes(data);

    // Pixel data was filled with (i % 256) in BuildMinimalGaf
    Assert.That(result.PixelData[0], Is.EqualTo(0));
    Assert.That(result.PixelData[1], Is.EqualTo(1));
    Assert.That(result.PixelData[2], Is.EqualTo(2));
    Assert.That(result.PixelData[3], Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRle_DecodesCorrectly() {
    var data = BuildRleGaf("rle_test", 4, 2, 9);
    var result = GafReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.PixelData.Length, Is.EqualTo(8));

    // Row 0: 2 transparent + 2 literal (0xAA, 0xBB)
    Assert.That(result.PixelData[0], Is.EqualTo(9));
    Assert.That(result.PixelData[1], Is.EqualTo(9));
    Assert.That(result.PixelData[2], Is.EqualTo(0xAA));
    Assert.That(result.PixelData[3], Is.EqualTo(0xBB));

    // Row 1: 4 literal (0x10, 0x20, 0x30, 0x40)
    Assert.That(result.PixelData[4], Is.EqualTo(0x10));
    Assert.That(result.PixelData[5], Is.EqualTo(0x20));
    Assert.That(result.PixelData[6], Is.EqualTo(0x30));
    Assert.That(result.PixelData[7], Is.EqualTo(0x40));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_ParsesCorrectly() {
    var data = BuildMinimalGaf("stream_test", 4, 4, 9, false);
    using var ms = new MemoryStream(data);
    var result = GafReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.Name, Is.EqualTo("stream_test"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroEntryCount_ThrowsInvalidDataException() {
    var data = new byte[100];
    BinaryPrimitives.WriteUInt32LittleEndian(data, 0x00010100); // magic
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 0); // entry_count = 0
    Assert.Throws<InvalidDataException>(() => GafReader.FromBytes(data));
  }

  /// <summary>Builds a minimal valid GAF with a single uncompressed frame.</summary>
  internal static byte[] BuildMinimalGaf(string name, int width, int height, byte transparencyIndex, bool compressed) {
    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms);

    // File header (12 bytes)
    bw.Write((uint)0x00010100); // version/magic
    bw.Write((uint)1);          // entry_count
    bw.Write((uint)0);          // reserved

    // Entry pointer table
    // Entry header starts at offset 16 (12 header + 4 pointer)
    var entryOffset = (uint)16;
    bw.Write(entryOffset);

    // Entry header (40 bytes) at offset 16
    bw.Write((ushort)1);        // frame_count
    bw.Write((ushort)0);        // unknown
    bw.Write((uint)0);          // unknown
    var nameBytes = new byte[32];
    var encoded = Encoding.ASCII.GetBytes(name.Length > 31 ? name[..31] : name);
    Array.Copy(encoded, nameBytes, Math.Min(encoded.Length, 31));
    bw.Write(nameBytes);

    // Frame pointer (at offset 56 = 16 + 40)
    var frameOffset = (uint)(56 + 4); // frame pointer table is 4 bytes, frame starts after
    bw.Write(frameOffset);

    // Frame header (20 bytes) at offset 60
    bw.Write((ushort)width);
    bw.Write((ushort)height);
    bw.Write((short)0);         // x_offset
    bw.Write((short)0);         // y_offset
    bw.Write(transparencyIndex);
    bw.Write((byte)(compressed ? 1 : 0));
    bw.Write((ushort)0);        // subframe_count = 0
    var dataOffset = (uint)(60 + 20); // pixel data starts right after frame header
    bw.Write(dataOffset);
    bw.Write((uint)0);          // unknown

    // Pixel data (uncompressed)
    var pixelCount = width * height;
    for (var i = 0; i < pixelCount; ++i)
      bw.Write((byte)(i % 256));

    return ms.ToArray();
  }

  /// <summary>Builds a minimal GAF with RLE-compressed pixel data.</summary>
  internal static byte[] BuildRleGaf(string name, int width, int height, byte transparencyIndex) {
    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms);

    // File header (12 bytes)
    bw.Write((uint)0x00010100);
    bw.Write((uint)1);
    bw.Write((uint)0);

    // Entry pointer
    var entryOffset = (uint)16;
    bw.Write(entryOffset);

    // Entry header (40 bytes)
    bw.Write((ushort)1);
    bw.Write((ushort)0);
    bw.Write((uint)0);
    var nameBytes = new byte[32];
    var encoded = Encoding.ASCII.GetBytes(name.Length > 31 ? name[..31] : name);
    Array.Copy(encoded, nameBytes, Math.Min(encoded.Length, 31));
    bw.Write(nameBytes);

    // Frame pointer
    var frameOffset = (uint)(56 + 4);
    bw.Write(frameOffset);

    // Frame header (20 bytes)
    bw.Write((ushort)width);
    bw.Write((ushort)height);
    bw.Write((short)0);
    bw.Write((short)0);
    bw.Write(transparencyIndex);
    bw.Write((byte)1);          // compressed = 1
    bw.Write((ushort)0);
    var dataOffset = (uint)(60 + 20);
    bw.Write(dataOffset);
    bw.Write((uint)0);

    // RLE pixel data
    // Row 0 (width=4): 2 transparent + 2 literal (0xAA, 0xBB)
    // Control bytes: 0x82 (transparent 2), 0x02 (literal 2), 0xAA, 0xBB = 5 bytes
    var row0 = new byte[] { 0x82, 0x02, 0xAA, 0xBB };
    bw.Write((ushort)row0.Length);
    bw.Write(row0);

    // Row 1 (width=4): 4 literal (0x10, 0x20, 0x30, 0x40)
    // Control bytes: 0x04 (literal 4), 0x10, 0x20, 0x30, 0x40 = 5 bytes
    var row1 = new byte[] { 0x04, 0x10, 0x20, 0x30, 0x40 };
    bw.Write((ushort)row1.Length);
    bw.Write(row1);

    return ms.ToArray();
  }
}
