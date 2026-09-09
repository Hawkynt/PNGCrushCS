using System;
using System.Buffers.Binary;
using FileFormat.IffSham;

namespace FileFormat.IffSham.Tests;

[TestFixture]
public sealed class IffShamWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesCanonicalIffShamChunks() {
    var file = _CreateStructuredFile();

    var bytes = IffShamWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes.AsSpan(0, 4).SequenceEqual("FORM"u8), Is.True);
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4, 4)), Is.EqualTo(bytes.Length - 8));
      Assert.That(bytes.AsSpan(8, 4).SequenceEqual("ILBM"u8), Is.True);
    });

    var bmhd = _FindChunk(bytes, "BMHD"u8);
    var camg = _FindChunk(bytes, "CAMG"u8);
    var cmap = _FindChunk(bytes, "CMAP"u8);
    var sham = _FindChunk(bytes, "SHAM"u8);
    var body = _FindChunk(bytes, "BODY"u8);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(bmhd, 2)), Is.EqualTo(320));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(bmhd + 2, 2)), Is.EqualTo(200));
      Assert.That(bytes[bmhd + 8], Is.EqualTo(6));
      Assert.That(bytes[bmhd + 10], Is.Zero);
      Assert.That(bytes[bmhd + 14], Is.EqualTo(10));
      Assert.That(bytes[bmhd + 15], Is.EqualTo(11));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(camg, 4)), Is.EqualTo(0x0000_0800u));
      Assert.That(bytes.AsSpan(cmap, 6).SequenceEqual(new byte[] { 0x11, 0x22, 0x33, 0xAA, 0xBB, 0xCC }), Is.True);
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(sham, 2)), Is.Zero);
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(sham + 2, 2)), Is.EqualTo(0x0123));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(sham + 4, 2)), Is.EqualTo(0x0ABC));
      Assert.That(_ChunkSize(bytes, "SHAM"u8), Is.EqualTo(2 + 200 * 16 * 2));
      Assert.That(_ChunkSize(bytes, "BODY"u8), Is.EqualTo(48_000));
      Assert.That(body + 48_000, Is.LessThanOrEqualTo(bytes.Length));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_InvalidPixelLength_ThrowsArgumentException() {
    var file = _CreateStructuredFile() with { PixelData = new byte[1] };

    Assert.Throws<ArgumentException>(() => IffShamWriter.ToBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_InvalidHamCommand_ThrowsArgumentException() {
    var file = _CreateStructuredFile();
    file.PixelData[123] = 0x40;

    Assert.Throws<ArgumentException>(() => IffShamWriter.ToBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LegacyRawOnlyModel_CopiesRawBytes() {
    var raw = new byte[12];
    "FORM"u8.CopyTo(raw);
    var file = new IffShamFile { RawData = raw };

    var result = IffShamWriter.ToBytes(file);
    raw[0] = 0;

    Assert.That(result[0], Is.EqualTo((byte)'F'));
  }

  private static IffShamFile _CreateStructuredFile() {
    var palettes = new byte[200 * 16 * 3];
    for (var y = 0; y < 200; ++y) {
      var at = y * 16 * 3;
      palettes[at] = 0x11;
      palettes[at + 1] = 0x22;
      palettes[at + 2] = 0x33;
      palettes[at + 3] = 0xAA;
      palettes[at + 4] = 0xBB;
      palettes[at + 5] = 0xCC;
    }

    return new() {
      Width = 320,
      Height = 200,
      RawData = [],
      PixelData = new byte[320 * 200],
      ScanlinePalettes = palettes,
    };
  }

  private static int _FindChunk(byte[] data, ReadOnlySpan<byte> id) {
    var end = 8 + BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4, 4));
    for (var offset = 12; offset + 8 <= end;) {
      var size = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset + 4, 4));
      if (data.AsSpan(offset, 4).SequenceEqual(id))
        return offset + 8;
      offset += 8 + size + (size & 1);
    }

    Assert.Fail($"Chunk {System.Text.Encoding.ASCII.GetString(id)} was not found.");
    return -1;
  }

  private static int _ChunkSize(byte[] data, ReadOnlySpan<byte> id) {
    var payload = _FindChunk(data, id);
    return BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(payload - 4, 4));
  }
}
