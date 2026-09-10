using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Cdr.Tests;

[TestFixture]
public sealed class CdrReaderWriterTests {

  [Test]
  [Category("Unit")]
  public void FromSpan_RiffCdr_PreservesOpaqueCmprListAndReadsVersion() {
    var listPayload = new byte[] { (byte)'c', (byte)'m', (byte)'p', (byte)'r', 0xDE, 0xAD, 0xBE, 0xEF, 0x13 };
    var bytes = _Riff("CDR9",
      ("vrsn", [0x84, 0x03]),
      ("LIST", listPayload),
      ("sumi", [0x42]));

    var file = CdrReader.FromSpan(bytes);

    Assert.Multiple(() => {
      Assert.That(file.FormType.ToString(), Is.EqualTo("CDR9"));
      Assert.That(file.Version, Is.EqualTo(900));
      Assert.That(file.Chunks.Select(static chunk => chunk.Id.ToString()), Is.EqualTo(new[] { "vrsn", "LIST", "sumi" }));
      Assert.That(file.Chunks[1].Data, Is.EqualTo(listPayload));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_Disp_IsBmpFileHeaderTailFollowedByPackedDib() {
    var image = _Image(0x10);
    var bmp = BmpWriter.ToBytes(BmpFile.FromRawImage(image));
    var bytes = _Riff("CDR9", ("vrsn", [0x84, 0x03]), ("DISP", bmp[10..]));

    var file = CdrReader.FromSpan(bytes);

    Assert.That(file.Preview, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(file.Preview!.Width, Is.EqualTo(image.Width));
      Assert.That(file.Preview.Height, Is.EqualTo(image.Height));
      Assert.That(file.Preview.PixelData, Is.EqualTo(image.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_WithoutVersionChunk_DerivesVersionFromForm() {
    var file = CdrReader.FromSpan(_Riff("CDRD", ("sumi", [0x00])));
    Assert.That(file.Version, Is.EqualTo(1300));
  }

  [TestCase("CDRH", 1700)]
  [TestCase("CDRI", 0)]
  [TestCase("CDRJ", 1800)]
  [TestCase("CDRK", 1900)]
  [Category("Unit")]
  public void FromSpan_LetterVersionForms_FollowCorelVersionMapping(string form, int expectedVersion) {
    var file = CdrReader.FromSpan(_Riff(form, ("sumi", [0x00])));
    Assert.That(file.Version, Is.EqualTo(expectedVersion));
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_LowercaseCdr8_IsVersion801AndCanBeRewritten() {
    var original = _Riff("cdr8", ("sumi", [0x42]));

    var file = CdrReader.FromSpan(original);
    var rewritten = CdrWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(file.FormType.ToString(), Is.EqualTo("cdr8"));
      Assert.That(file.Version, Is.EqualTo(801));
      Assert.That(rewritten, Is.EqualTo(original));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_OtherLowercaseCdrForm_Throws() {
    Assert.That(() => CdrReader.FromSpan(_Riff("cdr9", ("sumi", [0x42]))), Throws.TypeOf<InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_TruncatedChunk_Throws() {
    var bytes = _Riff("CDR9", ("sumi", [0x01, 0x02]));
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), uint.MaxValue);

    Assert.That(() => CdrReader.FromSpan(bytes), Throws.TypeOf<InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_NonCdrRiff_Throws() {
    Assert.That(() => CdrReader.FromSpan(_Riff("WAVE", ("fmt ", [0x00]))), Throws.TypeOf<InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WritesCdr4BitmapPageAndPreview() {
    var image = _Quadrants();

    var authored = CdrWriter.FromRawImage(image);
    var written = CdrWriter.ToBytes(authored);
    var reparsed = CdrReader.FromSpan(written);
    var bitmap = reparsed.Chunks.Single(static chunk => chunk.Id.ToString() == "bmp ").Data;
    var pageConfiguration = reparsed.Chunks.Single(static chunk => chunk.Id.ToString() == "mcfg").Data;
    var page = reparsed.Chunks.Single(static chunk => chunk.Id.ToString() == "LIST").Data;
    var expectedBitmap = BmpWriter.ToBytes(BmpFile.FromRawImage(image));

    Assert.Multiple(() => {
      Assert.That(authored.FormType.ToString(), Is.EqualTo("CDR4"));
      Assert.That(authored.Version, Is.EqualTo(400));
      Assert.That(reparsed.Version, Is.EqualTo(400));
      Assert.That(reparsed.Chunks.Select(static chunk => chunk.Id.ToString()),
        Is.EqualTo(new[] { "vrsn", "DISP", "mcfg", "bmp ", "LIST" }));
      Assert.That(reparsed.Preview, Is.Not.Null);
      Assert.That(reparsed.Preview!.PixelData, Is.EqualTo(image.PixelData));

      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bitmap), Is.EqualTo(1));
      Assert.That(bitmap.AsSpan(2).SequenceEqual(expectedBitmap), Is.True);
      Assert.That(bitmap.AsSpan(2, 2).SequenceEqual("BM"u8), Is.True);

      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(pageConfiguration), Is.EqualTo(2000));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(pageConfiguration.AsSpan(2)), Is.EqualTo(2000));

      Assert.That(page.AsSpan(0, 4).SequenceEqual("page"u8), Is.True);
      Assert.That(page.AsSpan(16, 4).SequenceEqual("LIST"u8), Is.True);
      Assert.That(page.AsSpan(24, 4).SequenceEqual("obj "u8), Is.True);
      Assert.That(page.AsSpan(28, 4).SequenceEqual("trfd"u8), Is.True);
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(54)), Is.EqualTo(1000));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(66)), Is.EqualTo(1000));
      Assert.That(page.AsSpan(70, 4).SequenceEqual("loda"u8), Is.True);
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(page.AsSpan(92)), Is.EqualTo(-2000));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(page.AsSpan(94)), Is.EqualTo(-2000));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(104)), Is.EqualTo(1));
    });
  }

  [TestCase(0, 1)]
  [TestCase(1, 0)]
  [TestCase(CdrFile.MaxDimension + 1, 1)]
  [TestCase(1, CdrFile.MaxDimension + 1)]
  [Category("Unit")]
  public void FromRawImage_UnsupportedDimensions_Throw(int width, int height) {
    var image = new RawImage { Width = width, Height = height, Format = PixelFormat.Bgr24, PixelData = [] };
    Assert.That(() => CdrWriter.FromRawImage(image), Throws.TypeOf<ArgumentOutOfRangeException>());
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ParsedContainer_PreservesTopLevelOrderOpaqueListsAndTrailingData() {
    var original = _Riff("CDR9",
      ("vrsn", [0x84, 0x03]),
      ("LIST", [(byte)'c', (byte)'m', (byte)'p', (byte)'r', 0x11, 0x22, 0x33]),
      ("sumi", [0x42]));
    original = [.. original, 0xFA, 0xCE];

    var file = CdrReader.FromSpan(original);
    var rewritten = CdrWriter.ToBytes(file);

    Assert.That(rewritten, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithPreview_WritesExactlyBmpBytesFromBfOffBitsFieldOnward() {
    var file = CdrReader.FromSpan(_Riff("CDR9",
      ("vrsn", [0x84, 0x03]),
      ("DISP", BmpWriter.ToBytes(BmpFile.FromRawImage(_Image(0x20)))[10..]),
      ("LIST", [(byte)'c', (byte)'m', (byte)'p', (byte)'r', 0x01])));
    var replacement = _Image(0x70);

    var written = CdrWriter.ToBytes(file, replacement);
    var reparsed = CdrReader.FromSpan(written);
    var expectedBmp = BmpWriter.ToBytes(BmpFile.FromRawImage(replacement));
    var disp = reparsed.Chunks.Single(static chunk => chunk.Id.ToString() == "DISP");

    Assert.Multiple(() => {
      Assert.That(disp.Data, Is.EqualTo(expectedBmp[10..]));
      Assert.That(reparsed.Preview, Is.Not.Null);
      Assert.That(reparsed.Preview!.PixelData, Is.EqualTo(replacement.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithPreviewAndNoDisp_InsertsItAfterVersion() {
    var file = CdrReader.FromSpan(_Riff("CDR9",
      ("vrsn", [0x84, 0x03]),
      ("LIST", [(byte)'c', (byte)'m', (byte)'p', (byte)'r', 0x01])));

    var written = CdrWriter.ToBytes(file, _Image(0x30));
    var reparsed = CdrReader.FromSpan(written);

    Assert.That(reparsed.Chunks.Select(static chunk => chunk.Id.ToString()), Is.EqualTo(new[] { "vrsn", "DISP", "LIST" }));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithRgb565Preview_UsesBitfieldMasksInBmpPixelOffset() {
    var image = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgb565,
      PixelData = [0x00, 0xF8, 0xE0, 0x07, 0x1F, 0x00, 0xFF, 0xFF],
    };
    var file = CdrReader.FromSpan(_Riff("CDR9", ("vrsn", [0x84, 0x03])));

    var written = CdrWriter.ToBytes(file, image);
    var disp = CdrReader.FromSpan(written).Chunks.Single(static chunk => chunk.Id.ToString() == "DISP").Data;
    var expectedBmp = BmpWriter.ToBytes(BmpFile.FromRawImage(image));

    Assert.That(disp, Is.EqualTo(expectedBmp[10..]));
  }

  private static RawImage _Image(byte seed) => new() {
    Width = 2,
    Height = 2,
    Format = PixelFormat.Bgr24,
    PixelData = [
      seed, (byte)(seed + 1), (byte)(seed + 2), (byte)(seed + 3), (byte)(seed + 4), (byte)(seed + 5),
      (byte)(seed + 6), (byte)(seed + 7), (byte)(seed + 8), (byte)(seed + 9), (byte)(seed + 10), (byte)(seed + 11),
    ],
  };

  private static RawImage _Quadrants() => new() {
    Width = 2,
    Height = 2,
    Format = PixelFormat.Bgr24,
    PixelData = [
      0x00, 0x00, 0xFF, 0x00, 0xFF, 0x00,
      0xFF, 0x00, 0x00, 0x00, 0xFF, 0xFF,
    ],
  };

  private static byte[] _Riff(string formType, params (string Id, byte[] Data)[] chunks) {
    using var stream = new MemoryStream();
    stream.Write(new byte[12]);

    foreach (var (id, payload) in chunks) {
      Span<byte> chunkHeader = stackalloc byte[8];
      _Ascii4(id, chunkHeader);
      BinaryPrimitives.WriteUInt32LittleEndian(chunkHeader[4..], checked((uint)payload.Length));
      stream.Write(chunkHeader);
      stream.Write(payload);
      if ((payload.Length & 1) != 0)
        stream.WriteByte(0);
    }

    var result = stream.ToArray();
    _Ascii4("RIFF", result);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), checked((uint)(result.Length - 8)));
    _Ascii4(formType, result.AsSpan(8));
    return result;
  }

  private static void _Ascii4(string value, Span<byte> destination) {
    if (value.Length != 4)
      throw new ArgumentException("Four ASCII bytes required.", nameof(value));

    for (var i = 0; i < 4; ++i)
      destination[i] = checked((byte)value[i]);
  }
}
