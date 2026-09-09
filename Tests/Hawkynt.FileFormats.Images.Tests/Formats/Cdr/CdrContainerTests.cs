using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.EmbeddedDib;
using FileFormat.Riff;

namespace FileFormat.Cdr.Tests;

[TestFixture]
public sealed class CdrContainerTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_DirectRiffFile_PreservesOpaqueCmprListAndReadsPreview() {
    var bytes = _Container(_Image());

    var file = CdrReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(file.FormType.ToString(), Is.EqualTo("CDr9"));
      Assert.That(file.Version, Is.EqualTo(900));
      Assert.That(file.Chunks.Count, Is.EqualTo(4));
      Assert.That(file.Chunks[0].Id.ToString(), Is.EqualTo("vrsn"));
      Assert.That(file.Chunks[1].Id.ToString(), Is.EqualTo("DISP"));
      Assert.That(file.Chunks[2].Id.ToString(), Is.EqualTo("LIST"));
      Assert.That(file.Chunks[2].Data, Is.EqualTo(_CompressedListBody()));
      Assert.That(file.Chunks[3].Id.ToString(), Is.EqualTo("sumi"));
      Assert.That(CdrFile.ToRawImage(file).PixelData, Is.EqualTo(_Image().PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_UnchangedParsedFile_RoundTripsWholeRiffContainer() {
    var bytes = _Container(_Image());
    var parsed = CdrReader.FromBytes(bytes);

    var rewritten = CdrWriter.ToBytes(parsed);

    Assert.That(rewritten, Is.EqualTo(bytes));
  }

  [Test]
  [Category("Unit")]
  public void WithPreview_ReplacesOnlyPackedDibAndPreservesCorelPrefixAndOtherChunks() {
    var original = CdrReader.FromBytes(_Container(_Image()));
    var replacement = new RawImage {
      Width = 1,
      Height = 2,
      Format = PixelFormat.Bgr24,
      PixelData = [0x30, 0x20, 0x10, 0x60, 0x50, 0x40],
    };
    var oldPrefix = original.Chunks[1].Data.AsSpan(0, sizeof(uint)).ToArray();

    var bytes = CdrWriter.ToBytes(original.WithPreview(replacement));
    var rewritten = CdrReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(rewritten.Chunks[1].Data.AsSpan(0, sizeof(uint)).ToArray(), Is.EqualTo(oldPrefix));
      Assert.That(rewritten.Chunks[2].Data, Is.EqualTo(_CompressedListBody()));
      Assert.That(rewritten.Chunks[3].Data, Is.EqualTo(original.Chunks[3].Data));
      Assert.That(rewritten.Preview, Is.Not.Null);
      Assert.That(rewritten.Preview!.Width, Is.EqualTo(replacement.Width));
      Assert.That(rewritten.Preview.Height, Is.EqualTo(replacement.Height));
      Assert.That(rewritten.Preview.Format, Is.EqualTo(replacement.Format));
      Assert.That(rewritten.Preview.PixelData, Is.EqualTo(replacement.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void MatchesSignature_AcceptsObservedAndDocumentedCdrFormsButRejectsCcx() {
    Assert.Multiple(() => {
      Assert.That(CdrFile.MatchesSignature(_Header("CDr9")), Is.True);
      Assert.That(CdrFile.MatchesSignature(_Header("CDR9")), Is.True);
      Assert.That(CdrFile.MatchesSignature(_Header("cdr8")), Is.True);
      Assert.That(CdrFile.MatchesSignature(_Header("CDRX")), Is.Null);
      Assert.That(CdrFile.MatchesSignature(_Header("WAVE")), Is.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZipWrappedModernCdr_IsRefusedByDirectRiffReader() {
    var zip = new byte[12];
    zip[0] = (byte)'P';
    zip[1] = (byte)'K';

    Assert.That(
      () => CdrReader.FromBytes(zip),
      Throws.TypeOf<InvalidDataException>()
        .With.Message.Contains("direct RIFF-based CorelDRAW")
    );
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TruncatedChunk_IsRejectedBeforeSlicing() {
    var bytes = _Container(_Image());
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), uint.MaxValue);

    Assert.That(
      () => CdrReader.FromBytes(bytes),
      Throws.TypeOf<InvalidDataException>().With.Message.Contains("beyond the end")
    );
  }

  [Test]
  [Category("Unit")]
  public void WithPreview_FileWithoutDisp_RefusesToInventContainerStructure() {
    var file = new CdrFile {
      FormType = "CDr9",
      Version = 900,
      Chunks = [new RiffChunk { Id = "vrsn", Data = _UInt16(900) }],
    };

    Assert.That(() => file.WithPreview(_Image()), Throws.InvalidOperationException);
  }

  private static byte[] _Container(RawImage preview) {
    var dib = EmbeddedDibWriter.ToBytes(preview);
    var display = new byte[sizeof(uint) + dib.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(display, 8);
    dib.CopyTo(display.AsSpan(sizeof(uint)));

    return RiffWriter.ToBytes(new RiffFile {
      FormType = "CDr9",
      Chunks = [
        new RiffChunk { Id = "vrsn", Data = _UInt16(900) },
        new RiffChunk { Id = "DISP", Data = display },
        // LIST cmpr is intentionally opaque here. Its bytes are compressed Corel data, not ordinary
        // recursively parseable RIFF child chunks.
        new RiffChunk { Id = "LIST", Data = _CompressedListBody() },
        new RiffChunk { Id = "sumi", Data = [0x10, 0x20, 0x30, 0x40] },
      ],
    });
  }

  private static byte[] _CompressedListBody()
    => [(byte)'c', (byte)'m', (byte)'p', (byte)'r', 0xFF, 0x00, 0xAA, 0x55, 0x13];

  private static byte[] _UInt16(ushort value) {
    var result = new byte[sizeof(ushort)];
    BinaryPrimitives.WriteUInt16LittleEndian(result, value);
    return result;
  }

  private static byte[] _Header(string form) {
    if (form.Length != 4)
      throw new ArgumentException("A RIFF form must contain four characters.", nameof(form));

    var result = new byte[RiffHeader.StructSize];
    "RIFF"u8.CopyTo(result);
    for (var i = 0; i < form.Length; ++i)
      result[8 + i] = (byte)form[i];

    return result;
  }

  private static RawImage _Image() => new() {
    Width = 2,
    Height = 2,
    Format = PixelFormat.Bgr24,
    PixelData = [
      0x03, 0x02, 0x01, 0x06, 0x05, 0x04,
      0x09, 0x08, 0x07, 0x0C, 0x0B, 0x0A,
    ],
  };

}
