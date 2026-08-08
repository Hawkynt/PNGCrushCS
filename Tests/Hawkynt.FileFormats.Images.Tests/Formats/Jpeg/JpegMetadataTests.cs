using System.Linq;
using FileFormat.Core;
using FileFormat.Jpeg;
using Hawkynt.FileFormats.Images;

namespace FileFormat.Jpeg.Tests;

/// <summary>Exercises <c>JpegMetadataCodec</c> indirectly through the public <see cref="JpegFile"/> /
/// <see cref="RawImage"/> surface.</summary>
[TestFixture]
public sealed class JpegMetadataTests {

  private static RawImage _TinyImage(ImageMetadata? metadata) => new() {
    Width = 8,
    Height = 8,
    Format = PixelFormat.Rgb24,
    PixelData = new byte[8 * 8 * 3],
    Metadata = metadata,
  };

  [Test]
  public void FromRawImage_ThenToRawImage_RoundTripsExif() {
    var exif = new ExifData {
      LittleEndian = true,
      Ifd0 = new ExifIfd { Entries = [new ExifTagEntry(ExifData.TagMake, ExifTagType.Ascii, 5, "ACME\0"u8.ToArray())] },
    };
    var raw = _TinyImage(new ImageMetadata { Exif = exif });

    var bytes = JpegWriter.ToBytes(JpegFile.FromRawImage(raw));
    var roundTripped = JpegFile.ToRawImage(JpegReader.FromBytes(bytes));

    Assert.That(roundTripped.Metadata, Is.Not.Null);
    var make = roundTripped.Metadata!.Exif!.Ifd0.Find(ExifData.TagMake);
    Assert.That(ExifData.DecodeAscii(make!), Is.EqualTo("ACME"));
  }

  [Test]
  public void FromRawImage_WritesExifAsApp1Segment() {
    var exif = new ExifData { LittleEndian = true, Ifd0 = new ExifIfd() };
    var bytes = JpegWriter.ToBytes(JpegFile.FromRawImage(_TinyImage(new ImageMetadata { Exif = exif })));

    var chunkNames = FormatRegistry.EnumerateChunks(bytes).Select(c => c.Name).ToList();
    Assert.That(chunkNames, Contains.Item("APP1"));
  }

  [Test]
  public void FromRawImage_ThenToRawImage_RoundTripsXmp() {
    var xmp = System.Text.Encoding.UTF8.GetBytes("<x:xmpmeta xmlns:x='adobe:ns:meta/'/>");
    var bytes = JpegWriter.ToBytes(JpegFile.FromRawImage(_TinyImage(new ImageMetadata { XmpPacket = xmp })));
    var roundTripped = JpegFile.ToRawImage(JpegReader.FromBytes(bytes));

    Assert.That(roundTripped.Metadata!.XmpPacket, Is.EqualTo(xmp));
  }

  [Test]
  public void FromRawImage_ThenToRawImage_RoundTripsIptc() {
    var iptc = new IptcData {
      DataSets = [new IptcDataSet(IptcData.RecordApplication, IptcData.DataSetObjectName, "Sunset"u8.ToArray())],
    };
    var bytes = JpegWriter.ToBytes(JpegFile.FromRawImage(_TinyImage(new ImageMetadata { Iptc = iptc })));
    var roundTripped = JpegFile.ToRawImage(JpegReader.FromBytes(bytes));

    Assert.That(roundTripped.Metadata!.Iptc!.GetString(IptcData.RecordApplication, IptcData.DataSetObjectName), Is.EqualTo("Sunset"));
  }

  [Test]
  public void FromRawImage_ThenToRawImage_RoundTripsCommentAsCom() {
    var bytes = JpegWriter.ToBytes(JpegFile.FromRawImage(_TinyImage(new ImageMetadata {
      TextEntries = [new TextMetadataEntry("Comment", "hello jpeg")],
    })));

    Assert.That(FormatRegistry.EnumerateChunks(bytes).Select(c => c.Name), Contains.Item("COM"));

    var roundTripped = JpegFile.ToRawImage(JpegReader.FromBytes(bytes));
    Assert.That(roundTripped.Metadata!.TextEntries.Single().Text, Is.EqualTo("hello jpeg"));
  }

  [Test]
  public void FromRawImage_NonCommentKeywordPrefixesComText() {
    // JPEG COM has no keyword slot, so a PNG-style keyword (e.g. "Title") is folded into the text
    // itself rather than silently dropped.
    var bytes = JpegWriter.ToBytes(JpegFile.FromRawImage(_TinyImage(new ImageMetadata {
      TextEntries = [new TextMetadataEntry("Title", "My Photo")],
    })));

    var roundTripped = JpegFile.ToRawImage(JpegReader.FromBytes(bytes));
    Assert.That(roundTripped.Metadata!.TextEntries.Single().Text, Is.EqualTo("Title: My Photo"));
  }

  [Test]
  public void ToRawImage_PlainDecode_NoMetadata_MetadataIsNull() {
    var bytes = JpegWriter.ToBytes(JpegFile.FromRawImage(_TinyImage(null)));
    var roundTripped = JpegFile.ToRawImage(JpegReader.FromBytes(bytes));
    Assert.That(roundTripped.Metadata, Is.Null);
  }

  [Test]
  public void ToBytes_PlainDecodeWithoutTouchingMetadata_IsByteIdenticalLosslessTranscode() {
    // The critical non-regression: decoding an existing JPEG and writing it straight back out (never
    // touching Metadata) must still take the byte-exact lossless-transcode path other tests depend on.
    var original = JpegWriter.ToBytes(JpegFile.FromRawImage(_TinyImage(new ImageMetadata {
      TextEntries = [new TextMetadataEntry("Comment", "original")],
    })));

    var decoded = JpegReader.FromBytes(original);
    Assert.That(decoded.Metadata, Is.Null, "JpegFile.Metadata itself must stay null after a plain decode");

    var rewritten = JpegWriter.ToBytes(decoded);
    Assert.That(rewritten, Is.EqualTo(decoded.RawJpegBytes), "an untouched round trip must reuse RawJpegBytes verbatim via lossless transcode");
  }

  [Test]
  public void ToBytes_ExplicitMetadataOverride_ReplacesEmbeddedComment() {
    var original = JpegWriter.ToBytes(JpegFile.FromRawImage(_TinyImage(new ImageMetadata {
      TextEntries = [new TextMetadataEntry("Comment", "original")],
    })));

    var decoded = JpegReader.FromBytes(original);
    var edited = decoded with { Metadata = new ImageMetadata { TextEntries = [new TextMetadataEntry("Comment", "edited")] } };
    var rewritten = JpegWriter.ToBytes(edited);

    var reread = JpegFile.ToRawImage(JpegReader.FromBytes(rewritten));
    Assert.That(reread.Metadata!.TextEntries.Single().Text, Is.EqualTo("edited"));
  }

  [Test]
  public void ToBytes_ExplicitMetadataOverride_CanStripAllText() {
    var original = JpegWriter.ToBytes(JpegFile.FromRawImage(_TinyImage(new ImageMetadata {
      TextEntries = [new TextMetadataEntry("Comment", "secret")],
    })));

    var decoded = JpegReader.FromBytes(original);
    var stripped = decoded with { Metadata = new ImageMetadata() }; // explicit, empty — "strip everything this codec knows about"
    var rewritten = JpegWriter.ToBytes(stripped);

    Assert.That(FormatRegistry.EnumerateChunks(rewritten).Select(c => c.Name), Does.Not.Contain("COM"));
  }
}
