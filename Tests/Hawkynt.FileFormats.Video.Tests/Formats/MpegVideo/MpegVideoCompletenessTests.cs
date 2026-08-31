using System.IO;
using System.Linq;
using FileFormat.Codecs.Mpeg.Tests;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.MpegVideo.Tests;

[TestFixture]
public sealed class MpegVideoCompletenessTests {

  [Test]
  [Category("Unit")]
  public void SequenceEndCodeSurvivesDemuxRemux() {
    var source = _Stream(withSequenceEnd: true);
    var container = MpegVideoReader.FromBytes(source);
    var packets = MpegVideoContainer.ReadPackets(container).ToArray();

    Assert.That(packets[^1].ContainerPrivateData.ToArray(), Is.EqualTo(new byte[] { 0x00, 0x00, 0x01, 0xB7 }));

    var remuxed = VideoIO.Mux<MpegVideoWriter>(MpegVideoContainer.Streams(container), packets);
    Assert.That(remuxed, Is.EqualTo(source));
  }

  [Test]
  [Category("Unit")]
  public void MissingSequenceEndCodeIsNotInvented() {
    var source = _Stream(withSequenceEnd: false);
    var container = MpegVideoReader.FromBytes(source);
    var packets = MpegVideoContainer.ReadPackets(container).ToArray();

    Assert.That(packets[^1].ContainerPrivateData.IsEmpty, Is.True);

    var remuxed = VideoIO.Mux<MpegVideoWriter>(MpegVideoContainer.Streams(container), packets);
    Assert.That(remuxed, Is.EqualTo(source));
  }

  [Test]
  [Category("Unit")]
  public void ConcatenatedSequencesAreNotDiscarded() {
    var first = _Stream(withSequenceEnd: true);
    var second = _Stream(withSequenceEnd: true);
    var source = first.Concat(second).ToArray();
    var container = MpegVideoReader.FromBytes(source);
    var packets = MpegVideoContainer.ReadPackets(container).ToArray();

    Assert.That(packets.Length, Is.EqualTo(2));
    Assert.That(packets.All(packet => packet.ContainerPrivateData.Span.SequenceEqual(new byte[] { 0x00, 0x00, 0x01, 0xB7 })), Is.True);

    var remuxed = VideoIO.Mux<MpegVideoWriter>(MpegVideoContainer.Streams(container), packets);
    Assert.That(remuxed, Is.EqualTo(source));
  }

  [TestCase("MPG1")]
  [TestCase("mpg1")]
  [TestCase("MPG2")]
  [TestCase("mpg2")]
  [Category("Unit")]
  public void WriterAcceptsConventionalMpegCodecTagCasing(string codec) {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters(codec),
    };

    var bytes = VideoIO.Mux<MpegVideoWriter>([stream], [new CodedPacket(0, new byte[] { 0x00, 0x00, 0x01, 0x00 })]);
    Assert.That(bytes, Is.EqualTo(new byte[] { 0x00, 0x00, 0x01, 0x00 }));
  }

  [Test]
  [Category("Unit")]
  public void WriterRefusesCodecPrivateDataThatRawElementaryVideoCannotCarry() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("MPG1"),
      CodecPrivateData = new byte[] { 1, 2, 3 },
    };

    Assert.That(
      () => MpegVideoWriter.Create([stream], VideoMetadata.Empty),
      Throws.TypeOf<NotSupportedException>().With.Message.Contains("cannot carry"));
  }

  [Test]
  [Category("Unit")]
  public void WriterRefusesUnknownPacketPrivateDataInsteadOfAppendingItSilently() {
    var writer = MpegVideoWriter.Create([_VideoStream()], VideoMetadata.Empty);

    Assert.That(
      () => writer.WritePacket(new CodedPacket(
        0,
        new byte[] { 0x00, 0x00, 0x01, 0x00 },
        ContainerPrivateData: new byte[] { 1, 2, 3, 4 })),
      Throws.TypeOf<InvalidDataException>().With.Message.Contains("sequence-end"));
  }

  [Test]
  [Category("Unit")]
  public void TruncatedOpeningSequenceHeaderIsRefusedBeforePacketWalking() {
    Assert.That(
      () => MpegVideoReader.FromBytes([0x00, 0x00, 0x01, 0xB3, 0x01, 0x00, 0x10]),
      Throws.TypeOf<InvalidDataException>().With.Message.Contains("Truncated MPEG sequence header"));
  }

  private static MediaStreamInfo _VideoStream()
    => new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("MPG1"),
    };

  private static byte[] _Stream(bool withSequenceEnd) {
    var stream = new MpegTestStream().SequenceHeader(16, 16).GroupOfPictures();
    _Picture(stream);
    return withSequenceEnd ? stream.End() : stream.ToArray();
  }

  private static void _Picture(MpegTestStream stream) {
    stream.PictureHeader(1).SliceHeader(0, 1).Code("1").Code("1");
    stream
      .IntraBlock(true, 0).IntraBlock(true, 0).IntraBlock(true, 0).IntraBlock(true, 0)
      .IntraBlock(false, 0).IntraBlock(false, 0);
  }
}
