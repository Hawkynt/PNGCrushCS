using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.H263Video;

namespace Hawkynt.FileFormats.Video.Tests.Formats.H263Video;

[TestFixture]
public sealed class H263VideoContainerTests {

  [Test]
  [Category("Unit")]
  public void PictureStartCodeSplitsTwoPicturesWithoutCopyingFramingAway() {
    // PSC is 22 bits: 0000 0000 0000 0000 1000 00; the low two bits of byte 2 already belong to TR.
    var first = new byte[] { 0x00, 0x00, 0x80, 0x12, 0x34, 0x56 };
    var second = new byte[] { 0x00, 0x00, 0x83, 0xAA, 0xBB };
    var file = first.Concat(second).ToArray();

    Assert.That(H263VideoContainer.MatchesSignature(file), Is.True);
    var container = H263VideoContainer.FromBytes(file);
    var stream = H263VideoContainer.Streams(container).Single();
    var packets = H263VideoContainer.ReadPackets(container).ToArray();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("H263")));
      Assert.That(stream.CodecId, Is.EqualTo("H263"));
      Assert.That(packets, Has.Length.EqualTo(2));
      Assert.That(packets[0].Data.ToArray(), Is.EqualTo(first));
      Assert.That(packets[1].Data.ToArray(), Is.EqualTo(second));
      Assert.That(packets.All(packet => packet.PresentationTimestamp is null), Is.True,
        "a raw H.263 elementary stream carries no presentation timestamps");
      Assert.That(packets.Select(packet => packet.DecodeTimestamp).ToArray(), Is.EqualTo(new long?[] { 0, 1 }),
        "coded order is known even though presentation timing is not");
      Assert.That(packets.All(packet => packet.Duration is null), Is.True,
        "there is no container time base from which to invent a duration");
      Assert.That(packets.All(packet => !packet.IsKeyFrame), Is.True,
        "framing alone does not parse the H.263 picture type");
    });
  }

  [Test]
  [Category("Unit")]
  public void WriterRoundTripConcatenatesCompletePictures() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("h263"),
    };
    var first = new byte[] { 0x00, 0x00, 0x81, 0x11, 0x22 };
    var second = new byte[] { 0x00, 0x00, 0x82, 0x33, 0x44, 0x55 };

    var file = VideoIO.Mux<H263VideoWriter>(
      [stream],
      [new CodedPacket(0, first), new CodedPacket(0, second)]);

    Assert.That(file, Is.EqualTo(first.Concat(second).ToArray()));
    var packets = H263VideoContainer.ReadPackets(H263VideoContainer.FromBytes(file)).ToArray();
    Assert.Multiple(() => {
      Assert.That(packets, Has.Length.EqualTo(2));
      Assert.That(packets[0].Data.ToArray(), Is.EqualTo(first));
      Assert.That(packets[1].Data.ToArray(), Is.EqualTo(second));
    });
  }

  [Test]
  [Category("Unit")]
  public void SignatureRequiresAllTwentyTwoPictureStartBits() {
    Assert.Multiple(() => {
      Assert.That(H263VideoContainer.MatchesSignature(new byte[] { 0x00, 0x00, 0x80 }), Is.True);
      Assert.That(H263VideoContainer.MatchesSignature(new byte[] { 0x00, 0x00, 0x83 }), Is.True);
      Assert.That(H263VideoContainer.MatchesSignature(new byte[] { 0x00, 0x00, 0x84 }), Is.Null);
      Assert.That(H263VideoContainer.MatchesSignature(new byte[] { 0x00, 0x01, 0x80 }), Is.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void ReaderAndWriterRejectDataWithoutPictureStartCode() {
    Assert.Throws<InvalidDataException>(() => H263VideoContainer.FromBytes(new byte[] { 0, 0, 0x84, 1 }));

    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("H263"),
    };
    var writer = H263VideoWriter.Create([stream], VideoMetadata.Empty);
    Assert.Throws<InvalidDataException>(() => writer.WritePacket(new CodedPacket(0, new byte[] { 1, 2, 3 })));
  }
}
