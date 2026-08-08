using System.Linq;
using FileFormat.Core;
using FileFormat.Jpeg;
using FileFormat.Png;

namespace Hawkynt.FileFormats.Images.Tests.Metadata;

/// <summary>PNG -> RawImage -> JPEG -> RawImage -> PNG round trips, proving metadata survives a hop
/// through a second format where both sides can hold it, and is explicitly (not silently) dropped
/// where the target format has no carrier.</summary>
[TestFixture]
public sealed class MetadataCrossFormatTests {

  private static RawImage _TinyImage(ImageMetadata? metadata) => new() {
    Width = 8,
    Height = 8,
    Format = PixelFormat.Rgb24,
    PixelData = new byte[8 * 8 * 3],
    Metadata = metadata,
  };

  private static ExifData _SampleExif() => new() {
    LittleEndian = true,
    Ifd0 = new ExifIfd {
      Entries = [
        new ExifTagEntry(ExifData.TagMake, ExifTagType.Ascii, 5, "ACME\0"u8.ToArray()),
        new ExifTagEntry(ExifData.TagModel, ExifTagType.Ascii, 6, "X-100\0"u8.ToArray()),
      ],
    },
  };

  [Test]
  public void PngToJpegToPng_ExifSurvivesBothHops() {
    var pngBytes = PngWriter.ToBytes(PngFile.FromRawImage(_TinyImage(new ImageMetadata { Exif = _SampleExif() })));
    var afterPng = PngFile.ToRawImage(PngReader.FromBytes(pngBytes));

    var jpegBytes = JpegWriter.ToBytes(JpegFile.FromRawImage(afterPng));
    var afterJpeg = JpegFile.ToRawImage(JpegReader.FromBytes(jpegBytes));

    var pngAgainBytes = PngWriter.ToBytes(PngFile.FromRawImage(afterJpeg));
    var afterSecondPng = PngFile.ToRawImage(PngReader.FromBytes(pngAgainBytes));

    var make = afterSecondPng.Metadata!.Exif!.Ifd0.Find(ExifData.TagMake);
    Assert.That(ExifData.DecodeAscii(make!), Is.EqualTo("ACME"));
    var model = afterSecondPng.Metadata.Exif.Ifd0.Find(ExifData.TagModel);
    Assert.That(ExifData.DecodeAscii(model!), Is.EqualTo("X-100"));
  }

  [Test]
  public void PngToJpeg_CommentTextSurvives() {
    var pngBytes = PngWriter.ToBytes(PngFile.FromRawImage(_TinyImage(new ImageMetadata {
      TextEntries = [new TextMetadataEntry("Comment", "a shared comment")],
    })));
    var afterPng = PngFile.ToRawImage(PngReader.FromBytes(pngBytes));

    var jpegBytes = JpegWriter.ToBytes(JpegFile.FromRawImage(afterPng));
    var afterJpeg = JpegFile.ToRawImage(JpegReader.FromBytes(jpegBytes));

    Assert.That(afterJpeg.Metadata!.TextEntries.Single().Text, Is.EqualTo("a shared comment"));
  }

  [Test]
  public void JpegToPngToJpeg_XmpSurvivesBothHops() {
    var xmp = System.Text.Encoding.UTF8.GetBytes("<x:xmpmeta xmlns:x='adobe:ns:meta/'/>");
    var jpegBytes = JpegWriter.ToBytes(JpegFile.FromRawImage(_TinyImage(new ImageMetadata { XmpPacket = xmp })));
    var afterJpeg = JpegFile.ToRawImage(JpegReader.FromBytes(jpegBytes));

    var pngBytes = PngWriter.ToBytes(PngFile.FromRawImage(afterJpeg));
    var afterPng = PngFile.ToRawImage(PngReader.FromBytes(pngBytes));

    var jpegAgainBytes = JpegWriter.ToBytes(JpegFile.FromRawImage(afterPng));
    var afterSecondJpeg = JpegFile.ToRawImage(JpegReader.FromBytes(jpegAgainBytes));

    Assert.That(afterSecondJpeg.Metadata!.XmpPacket, Is.EqualTo(xmp));
  }

  [Test]
  public void PngToJpeg_IccProfileIsExplicitlyDropped_NotFabricated() {
    // JPEG APP2 ICC_PROFILE carriage is out of scope for this codec (see JpegMetadataCodec remarks) —
    // the profile must disappear cleanly rather than surface as some mangled substitute.
    var profile = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
    var pngBytes = PngWriter.ToBytes(PngFile.FromRawImage(_TinyImage(new ImageMetadata { IccProfile = profile, IccProfileName = "Test" })));
    var afterPng = PngFile.ToRawImage(PngReader.FromBytes(pngBytes));
    Assert.That(afterPng.Metadata!.IccProfile, Is.EqualTo(profile), "sanity: PNG side actually has it");

    var jpegBytes = JpegWriter.ToBytes(JpegFile.FromRawImage(afterPng));
    var afterJpeg = JpegFile.ToRawImage(JpegReader.FromBytes(jpegBytes));

    Assert.That(afterJpeg.Metadata is null || afterJpeg.Metadata.IccProfile is null, Is.True);
  }

  [Test]
  public void JpegToPng_IptcIsExplicitlyDropped_NotFabricated() {
    // PNG has no standard IPTC carrier chunk — the dataset must disappear cleanly on the PNG hop.
    var iptc = new IptcData {
      DataSets = [new IptcDataSet(IptcData.RecordApplication, IptcData.DataSetObjectName, "Sunset"u8.ToArray())],
    };
    var jpegBytes = JpegWriter.ToBytes(JpegFile.FromRawImage(_TinyImage(new ImageMetadata { Iptc = iptc })));
    var afterJpeg = JpegFile.ToRawImage(JpegReader.FromBytes(jpegBytes));
    Assert.That(afterJpeg.Metadata!.Iptc, Is.Not.Null, "sanity: JPEG side actually has it");

    var pngBytes = PngWriter.ToBytes(PngFile.FromRawImage(afterJpeg));
    var afterPng = PngFile.ToRawImage(PngReader.FromBytes(pngBytes));

    Assert.That(afterPng.Metadata is null || afterPng.Metadata.Iptc is null, Is.True);
  }

  [Test]
  public void PngToJpeg_NonCommentKeywordSurvivesAsPrefixedComment() {
    var pngBytes = PngWriter.ToBytes(PngFile.FromRawImage(_TinyImage(new ImageMetadata {
      TextEntries = [new TextMetadataEntry("Author", "Jane Doe")],
    })));
    var afterPng = PngFile.ToRawImage(PngReader.FromBytes(pngBytes));

    var jpegBytes = JpegWriter.ToBytes(JpegFile.FromRawImage(afterPng));
    var afterJpeg = JpegFile.ToRawImage(JpegReader.FromBytes(jpegBytes));

    // JPEG COM has no keyword slot; the keyword is folded into the text rather than lost.
    Assert.That(afterJpeg.Metadata!.TextEntries.Single().Text, Is.EqualTo("Author: Jane Doe"));
  }
}
