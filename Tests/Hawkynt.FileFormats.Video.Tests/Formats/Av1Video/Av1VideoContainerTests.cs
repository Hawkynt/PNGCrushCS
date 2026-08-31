using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Av1Video.Tests;

[TestFixture]
public sealed class Av1VideoContainerTests {

  [Test]
  [Category("Unit")]
  public void Signature_RecognizesCanonicalTemporalDelimiter() {
    Assert.That(Av1VideoContainer.MatchesSignature([0x12, 0x00, 0x32, 0x01, 0xAA]), Is.True);
    Assert.That(Av1VideoContainer.MatchesSignature([0x32, 0x01, 0xAA]), Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void Reader_SplitsAtTemporalDelimiterObusWithoutInventingPresentationTiming() {
    byte[] file = [
      0x12, 0x00,
      0x0A, 0x01, 0x00,
      0x32, 0x02, 0xAA, 0xBB,
      0x12, 0x00,
      0x32, 0x01, 0xCC,
    ];

    var container = Av1VideoContainer.FromBytes(file);
    var packets = Av1VideoContainer.ReadPackets(container).ToArray();

    Assert.That(packets, Has.Length.EqualTo(2));
    Assert.That(packets[0].Data.ToArray(), Is.EqualTo(file[..9]));
    Assert.That(packets[1].Data.ToArray(), Is.EqualTo(file[9..]));
    Assert.That(packets.Select(packet => packet.DecodeTimestamp), Is.EqualTo(new long?[] { 0, 1 }));
    Assert.That(packets.All(packet => packet.PresentationTimestamp is null && packet.Duration is null), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void Writer_InsertsTemporalDelimiterForAnUndelimitedTemporalUnit() {
    var stream = _Av1Stream();
    var file = VideoIO.Mux<Av1VideoWriter>(
      [stream],
      [new CodedPacket(0, new byte[] { 0x32, 0x02, 0xAA, 0xBB })]);

    Assert.That(file, Is.EqualTo(new byte[] { 0x12, 0x00, 0x32, 0x02, 0xAA, 0xBB }));
  }

  [Test]
  [Category("Unit")]
  public void Writer_PreservesAnExistingTemporalDelimiter() {
    var stream = _Av1Stream();
    byte[] unit = [0x12, 0x00, 0x32, 0x01, 0xAA];

    var file = VideoIO.Mux<Av1VideoWriter>([stream], [new CodedPacket(0, unit)]);

    Assert.That(file, Is.EqualTo(unit));
  }

  [Test]
  [Category("Unit")]
  public void Reader_RejectsObuWithoutRequiredLowOverheadSizeField() {
    byte[] file = [0x12, 0x00, 0x30, 0xAA];
    var container = Av1VideoContainer.FromBytes(file);

    var failure = Assert.Throws<InvalidDataException>(() => Av1VideoContainer.ReadPackets(container).ToArray());

    Assert.That(failure!.Message, Does.Contain("obu_size"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_RejectsTruncatedLeb128() {
    byte[] file = [0x12, 0x00, 0x32, 0x80];
    var container = Av1VideoContainer.FromBytes(file);

    var failure = Assert.Throws<InvalidDataException>(() => Av1VideoContainer.ReadPackets(container).ToArray());

    Assert.That(failure!.Message, Does.Contain("LEB128"));
  }

  [Test]
  [Category("Unit")]
  public void Writer_RejectsPacketContainingMoreThanOneTemporalUnit() {
    var stream = _Av1Stream();
    byte[] twoUnits = [0x12, 0x00, 0x32, 0x01, 0xAA, 0x12, 0x00, 0x32, 0x01, 0xBB];

    var failure = Assert.Throws<InvalidDataException>(() =>
      VideoIO.Mux<Av1VideoWriter>([stream], [new CodedPacket(0, twoUnits)]));

    Assert.That(failure!.Message, Does.Contain("second temporal unit"));
  }

  [Test]
  [Category("Unit")]
  public void Writer_RejectsCodecPrivateDataBecauseRawObuHasNowhereToStoreIt() {
    var stream = _Av1Stream() with { CodecPrivateData = new byte[] { 1, 2, 3 } };

    Assert.Throws<NotSupportedException>(() =>
      VideoIO.Mux<Av1VideoWriter>([stream], [new CodedPacket(0, new byte[] { 0x32, 0x01, 0xAA })]));
  }

  private static MediaStreamInfo _Av1Stream()
    => new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("av01"),
    };
}
