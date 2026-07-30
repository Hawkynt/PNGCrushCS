using System;
using FileFormat.Jbig2;

namespace FileFormat.Jbig2.Tests;

[TestFixture]
public sealed class Jbig2SegmentTests {

  [Test]
  [Category("Unit")]
  public void ParsedFile_HasPageInfoSegment() {
    var file = new Jbig2File {
      Width = 16,
      Height = 4,
      PixelData = new byte[8],
    };

    var bytes = Jbig2Writer.ToBytes(file);
    var result = Jbig2Reader.FromBytes(bytes);

    var hasPageInfo = false;
    foreach (var seg in result.Segments)
      if (seg.Type == Jbig2SegmentType.PageInformation) {
        hasPageInfo = true;
        break;
      }

    Assert.That(hasPageInfo, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ParsedFile_HasGenericRegionSegment() {
    var file = new Jbig2File {
      Width = 16,
      Height = 4,
      PixelData = new byte[8],
    };

    var bytes = Jbig2Writer.ToBytes(file);
    var result = Jbig2Reader.FromBytes(bytes);

    var hasGenericRegion = false;
    foreach (var seg in result.Segments)
      if (seg.Type == Jbig2SegmentType.ImmediateLosslessGenericRegion) {
        hasGenericRegion = true;
        break;
      }

    Assert.That(hasGenericRegion, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ParsedFile_HasEndOfPageSegment() {
    var file = new Jbig2File {
      Width = 8,
      Height = 1,
      PixelData = [0x00],
    };

    var bytes = Jbig2Writer.ToBytes(file);
    var result = Jbig2Reader.FromBytes(bytes);

    var hasEndOfPage = false;
    foreach (var seg in result.Segments)
      if (seg.Type == Jbig2SegmentType.EndOfPage) {
        hasEndOfPage = true;
        break;
      }

    Assert.That(hasEndOfPage, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ParsedFile_HasEndOfFileSegment() {
    var file = new Jbig2File {
      Width = 8,
      Height = 1,
      PixelData = [0x00],
    };

    var bytes = Jbig2Writer.ToBytes(file);
    var result = Jbig2Reader.FromBytes(bytes);

    var hasEndOfFile = false;
    foreach (var seg in result.Segments)
      if (seg.Type == Jbig2SegmentType.EndOfFile) {
        hasEndOfFile = true;
        break;
      }

    Assert.That(hasEndOfFile, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void Segment_PageAssociation_CorrectForPageInfo() {
    var file = new Jbig2File {
      Width = 8,
      Height = 1,
      PixelData = [0x00],
    };

    var bytes = Jbig2Writer.ToBytes(file);
    var result = Jbig2Reader.FromBytes(bytes);

    foreach (var seg in result.Segments)
      if (seg.Type == Jbig2SegmentType.PageInformation) {
        Assert.That(seg.PageAssociation, Is.EqualTo(1));
        return;
      }

    Assert.Fail("PageInformation segment not found.");
  }

  [Test]
  [Category("Unit")]
  public void Segment_PageAssociation_ZeroForEndOfFile() {
    var file = new Jbig2File {
      Width = 8,
      Height = 1,
      PixelData = [0x00],
    };

    var bytes = Jbig2Writer.ToBytes(file);
    var result = Jbig2Reader.FromBytes(bytes);

    foreach (var seg in result.Segments)
      if (seg.Type == Jbig2SegmentType.EndOfFile) {
        Assert.That(seg.PageAssociation, Is.EqualTo(0));
        return;
      }

    Assert.Fail("EndOfFile segment not found.");
  }

  [Test]
  [Category("Unit")]
  public void Segment_SequentialNumbers() {
    var file = new Jbig2File {
      Width = 8,
      Height = 1,
      PixelData = [0x00],
    };

    var bytes = Jbig2Writer.ToBytes(file);
    var result = Jbig2Reader.FromBytes(bytes);

    for (var i = 0; i < result.Segments.Length; ++i)
      Assert.That(result.Segments[i].Number, Is.EqualTo(i));
  }
}
