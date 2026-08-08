using System;
using System.Linq;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests.Metadata;

[TestFixture]
public sealed class ExifCodecTests {

  private static ExifData _BuildSample() {
    var ifd0 = new ExifIfd {
      Entries = [
        new ExifTagEntry(ExifData.TagMake, ExifTagType.Ascii, 8, "ACME Co\0"u8.ToArray()),
        new ExifTagEntry(ExifData.TagModel, ExifTagType.Ascii, 6, "X-100\0"u8.ToArray()),
        new ExifTagEntry(ExifData.TagOrientation, ExifTagType.Short, 1, [1, 0]),
      ],
    };
    var exifIfd = new ExifIfd {
      Entries = [
        new ExifTagEntry(ExifData.TagExposureTime, ExifTagType.Rational, 1, _Rational(1, 250)),
        new ExifTagEntry(ExifData.TagDateTimeOriginal, ExifTagType.Ascii, 20, "2024:01:02 03:04:05\0"u8.ToArray()),
      ],
    };
    var gpsIfd = new ExifIfd {
      Entries = [
        new ExifTagEntry(ExifData.GpsTagLatitudeRef, ExifTagType.Ascii, 2, "N\0"u8.ToArray()),
      ],
    };

    return new ExifData { LittleEndian = true, Ifd0 = ifd0, ExifIfd = exifIfd, GpsIfd = gpsIfd };
  }

  private static byte[] _Rational(uint num, uint den) {
    var b = new byte[8];
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0), num);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), den);
    return b;
  }

  [Test]
  public void Write_Then_Parse_RoundTripsIfd0() {
    var original = _BuildSample();
    var bytes = ExifCodec.Write(original);
    var parsed = ExifCodec.TryParse(bytes);

    Assert.That(parsed, Is.Not.Null);
    Assert.That(parsed!.LittleEndian, Is.True);

    var make = parsed.Ifd0.Find(ExifData.TagMake);
    Assert.That(make, Is.Not.Null);
    Assert.That(ExifData.DecodeAscii(make!), Is.EqualTo("ACME Co"));

    var model = parsed.Ifd0.Find(ExifData.TagModel);
    Assert.That(ExifData.DecodeAscii(model!), Is.EqualTo("X-100"));

    var orientation = parsed.Ifd0.Find(ExifData.TagOrientation);
    Assert.That(parsed.DecodeShort(orientation!), Is.EqualTo(1));
  }

  [Test]
  public void Write_Then_Parse_RoundTripsExifSubIfd() {
    var bytes = ExifCodec.Write(_BuildSample());
    var parsed = ExifCodec.TryParse(bytes);

    Assert.That(parsed!.ExifIfd, Is.Not.Null);
    var exposure = parsed.ExifIfd!.Find(ExifData.TagExposureTime);
    var rational = parsed.DecodeRationals(exposure!).Single();
    Assert.That(rational.Numerator, Is.EqualTo(1u));
    Assert.That(rational.Denominator, Is.EqualTo(250u));

    var dto = parsed.ExifIfd.Find(ExifData.TagDateTimeOriginal);
    Assert.That(ExifData.DecodeAscii(dto!), Is.EqualTo("2024:01:02 03:04:05"));
  }

  [Test]
  public void Write_Then_Parse_RoundTripsGpsSubIfd() {
    var bytes = ExifCodec.Write(_BuildSample());
    var parsed = ExifCodec.TryParse(bytes);

    Assert.That(parsed!.GpsIfd, Is.Not.Null);
    var latRef = parsed.GpsIfd!.Find(ExifData.GpsTagLatitudeRef);
    Assert.That(ExifData.DecodeAscii(latRef!), Is.EqualTo("N"));
  }

  [Test]
  public void Write_OmitsSubIfdPointerTagsFromIfd0Entries() {
    // The Exif/GPS sub-IFD pointer tags are structural plumbing, not user data — they shouldn't
    // leak into the parsed Ifd0.Entries list a caller iterates for "real" tags.
    var bytes = ExifCodec.Write(_BuildSample());
    var parsed = ExifCodec.TryParse(bytes);

    Assert.That(parsed!.Ifd0.Find(ExifData.TagExifIfdPointer), Is.Null);
    Assert.That(parsed.Ifd0.Find(ExifData.TagGpsIfdPointer), Is.Null);
  }

  [Test]
  public void Write_TagsSortedAscendingWithinIfd() {
    // TIFF6 requires ascending tag order within an IFD; entries were built out of order on purpose.
    var ifd0 = new ExifIfd {
      Entries = [
        new ExifTagEntry(ExifData.TagCopyright, ExifTagType.Ascii, 2, "\0"u8.ToArray()),
        new ExifTagEntry(ExifData.TagMake, ExifTagType.Ascii, 2, "\0"u8.ToArray()),
        new ExifTagEntry(ExifData.TagArtist, ExifTagType.Ascii, 2, "\0"u8.ToArray()),
      ],
    };
    var bytes = ExifCodec.Write(new ExifData { LittleEndian = true, Ifd0 = ifd0 });
    var parsed = ExifCodec.TryParse(bytes)!;

    var tags = parsed.Ifd0.Entries.Select(e => e.Tag).ToList();
    var sorted = tags.OrderBy(t => t).ToList();
    Assert.That(tags, Is.EqualTo(sorted));
  }

  [Test]
  public void TryParse_RejectsTooShortBuffer() {
    Assert.That(ExifCodec.TryParse(new byte[4]), Is.Null);
  }

  [Test]
  public void TryParse_RejectsBadByteOrderMark() {
    var bytes = ExifCodec.Write(_BuildSample());
    bytes[0] = (byte)'X';
    Assert.That(ExifCodec.TryParse(bytes), Is.Null);
  }

  [Test]
  public void Write_BigEndianSourceStillReadsBackCorrectly() {
    // Parse a hand-built big-endian ("MM") TIFF blob to prove endian handling isn't hardcoded to "II".
    var data = new byte[] {
      (byte)'M', (byte)'M', 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08, // header, IFD0 @ 8
      0x00, 0x01,                                               // 1 entry
      0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, // Orientation SHORT=1
      0x00, 0x00, 0x00, 0x00,                                   // next IFD = 0
    };

    var parsed = ExifCodec.TryParse(data);
    Assert.That(parsed, Is.Not.Null);
    Assert.That(parsed!.LittleEndian, Is.False);
    var orientation = parsed.Ifd0.Find(ExifData.TagOrientation);
    Assert.That(parsed.DecodeShort(orientation!), Is.EqualTo(1));
  }

  [Test]
  public void TryParse_HugeComponentCount_DoesNotThrow() {
    // A corrupt/hostile Count field (here: 8 bytes/component * 0x30000000 components) overflows a
    // 32-bit byte-length computation. It must be treated as "doesn't fit" and skipped, not crash.
    var data = new byte[] {
      (byte)'I', (byte)'I', 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, // header, IFD0 @ 8
      0x01, 0x00,                                               // 1 entry
      0x01, 0x00, 0x05, 0x00, 0x00, 0x00, 0x00, 0x30, 0x00, 0x00, 0x00, 0x00, // tag=1 RATIONAL count=0x30000000
      0x00, 0x00, 0x00, 0x00,                                   // next IFD = 0
    };

    ExifData? parsed = null;
    Assert.DoesNotThrow(() => parsed = ExifCodec.TryParse(data));
    Assert.That(parsed, Is.Not.Null);
    Assert.That(parsed!.Ifd0.Entries, Has.Count.EqualTo(1));
  }
}
