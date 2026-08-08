using System.Linq;
using System.Text;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests.Metadata;

[TestFixture]
public sealed class IptcCodecTests {

  private static IptcData _BuildSample() => new() {
    DataSets = [
      new IptcDataSet(IptcData.RecordApplication, IptcData.DataSetObjectName, "Sunset"u8.ToArray()),
      new IptcDataSet(IptcData.RecordApplication, IptcData.DataSetKeywords, "beach"u8.ToArray()),
      new IptcDataSet(IptcData.RecordApplication, IptcData.DataSetKeywords, "vacation"u8.ToArray()),
      new IptcDataSet(IptcData.RecordApplication, IptcData.DataSetByLine, "J. Doe"u8.ToArray()),
    ],
  };

  [Test]
  public void ToPhotoshopSegment_Then_TryParse_RoundTrips() {
    var original = _BuildSample();
    var segment = IptcCodec.ToPhotoshopSegment(original);
    var parsed = IptcCodec.TryParsePhotoshopSegment(segment);

    Assert.That(parsed, Is.Not.Null);
    Assert.That(parsed!.GetString(IptcData.RecordApplication, IptcData.DataSetObjectName), Is.EqualTo("Sunset"));
    Assert.That(parsed.GetString(IptcData.RecordApplication, IptcData.DataSetByLine), Is.EqualTo("J. Doe"));
    Assert.That(parsed.GetStrings(IptcData.RecordApplication, IptcData.DataSetKeywords), Is.EqualTo(new[] { "beach", "vacation" }));
  }

  [Test]
  public void ToPhotoshopSegment_StartsWithPhotoshopSignature() {
    var segment = IptcCodec.ToPhotoshopSegment(_BuildSample());
    var sig = Encoding.ASCII.GetString(segment, 0, 13);
    Assert.That(sig, Is.EqualTo("Photoshop 3.0"));
  }

  [Test]
  public void TryParsePhotoshopSegment_RejectsWrongSignature() {
    var bogus = Encoding.ASCII.GetBytes("Not Photoshop\0garbage");
    Assert.That(IptcCodec.TryParsePhotoshopSegment(bogus), Is.Null);
  }

  [Test]
  public void TryParsePhotoshopSegment_ReturnsNullWhenNoIptcResource() {
    // A Photoshop segment carrying only an unrelated resource (not 0x0404) has no IPTC to extract.
    using var ms = new System.IO.MemoryStream();
    ms.Write("Photoshop 3.0\0"u8);
    ms.Write("8BIM"u8);
    ms.Write(new byte[] { 0x04, 0x0B }); // resource id 0x040B (something else), not 0x0404
    ms.WriteByte(0); ms.WriteByte(0);    // empty padded name
    ms.Write(new byte[] { 0, 0, 0, 2 }); // 2 bytes of data
    ms.Write(new byte[] { 0xAA, 0xBB });

    Assert.That(IptcCodec.TryParsePhotoshopSegment(ms.ToArray()), Is.Null);
  }

  [Test]
  public void ToPhotoshopSegment_EmptyDataSets_StillParsesToEmpty() {
    var segment = IptcCodec.ToPhotoshopSegment(new IptcData());
    var parsed = IptcCodec.TryParsePhotoshopSegment(segment);
    Assert.That(parsed, Is.Not.Null);
    Assert.That(parsed!.DataSets, Is.Empty);
  }
}
