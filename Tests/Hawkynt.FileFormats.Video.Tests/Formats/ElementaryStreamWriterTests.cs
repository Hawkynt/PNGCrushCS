using System;
using System.Linq;
using FileFormat.Core;
using FileFormat.H264Video;
using FileFormat.H265Video;
using FileFormat.Mjpeg;
using FileFormat.MpegVideo;

namespace Hawkynt.FileFormats.Video.Tests.Formats;

[TestFixture]
public sealed class ElementaryStreamWriterTests {

  private static MediaStreamInfo _Video(string codec, ReadOnlyMemory<byte> privateData = default)
    => new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters(codec),
      CodecPrivateData = privateData,
    };

  [Test]
  [Category("Unit")]
  public void H264MuxConcatenatesAnnexBPacketsByteForByte() {
    var first = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0x00, 0x1E };
    var second = new byte[] { 0x00, 0x00, 0x01, 0x65, 0x80 };

    var result = VideoIO.Mux<H264VideoWriter>(
      [_Video("avc1")],
      [new(0, first), new(0, second)]);

    Assert.That(result, Is.EqualTo(first.Concat(second).ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void H264MuxRefusesLengthPrefixedPacketsAndCodecPrivateData() {
    Assert.That(
      () => VideoIO.Mux<H264VideoWriter>([_Video("avc1")], [new(0, new byte[] { 0, 0, 0, 2, 0x65, 0x80 })]),
      Throws.TypeOf<System.IO.InvalidDataException>().With.Message.Contains("Annex B"));

    Assert.That(
      () => H264VideoWriter.Create([_Video("avc1", new byte[] { 1, 2, 3 })], VideoMetadata.Empty),
      Throws.TypeOf<NotSupportedException>());
  }

  [Test]
  [Category("Unit")]
  public void H265MuxConcatenatesAnnexBPacketsByteForByte() {
    var first = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x40, 0x01, 0x0C };
    var second = new byte[] { 0x00, 0x00, 0x01, 0x26, 0x01, 0x80 };

    var result = VideoIO.Mux<H265VideoWriter>(
      [_Video("hvc1")],
      [new(0, first), new(0, second)]);

    Assert.That(result, Is.EqualTo(first.Concat(second).ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void H265MuxRefusesLengthPrefixedPacketsAndCodecPrivateData() {
    Assert.That(
      () => VideoIO.Mux<H265VideoWriter>([_Video("hvc1")], [new(0, new byte[] { 0, 0, 0, 2, 0x26, 0x01 })]),
      Throws.TypeOf<System.IO.InvalidDataException>().With.Message.Contains("Annex B"));

    Assert.That(
      () => H265VideoWriter.Create([_Video("hvc1", new byte[] { 1, 2, 3 })], VideoMetadata.Empty),
      Throws.TypeOf<NotSupportedException>());
  }

  [Test]
  [Category("Unit")]
  public void MotionJpegMuxRequiresCompleteJpegPackets() {
    var first = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
    var second = new byte[] { 0xFF, 0xD8, 0x01, 0x02, 0xFF, 0xD9 };

    var result = VideoIO.Mux<MjpegWriter>(
      [_Video("MJPG")],
      [new(0, first), new(0, second)]);

    Assert.That(result, Is.EqualTo(first.Concat(second).ToArray()));
    Assert.That(
      () => VideoIO.Mux<MjpegWriter>([_Video("MJPG")], [new(0, new byte[] { 0xFF, 0xD8, 0x01, 0x02 })]),
      Throws.TypeOf<System.IO.InvalidDataException>().With.Message.Contains("complete JPEG"));
  }

  [Test]
  [Category("Unit")]
  public void MpegElementaryMuxPreservesPacketBytes() {
    var sequenceAndPicture = new byte[] { 0x00, 0x00, 0x01, 0xB3, 1, 2, 3, 4, 0x00, 0x00, 0x01, 0x00, 5, 6 };
    var nextPicture = new byte[] { 0x00, 0x00, 0x01, 0x00, 7, 8, 9 };

    var result = VideoIO.Mux<MpegVideoWriter>(
      [_Video("MPG1")],
      [new(0, sequenceAndPicture), new(0, nextPicture)]);

    Assert.That(result, Is.EqualTo(sequenceAndPicture.Concat(nextPicture).ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void RawStreamMuxersRejectAnIncompatibleStreamShape() {
    Assert.That(
      () => H264VideoWriter.Create([], VideoMetadata.Empty),
      Throws.TypeOf<NotSupportedException>().With.Message.Contains("exactly one stream"));

    Assert.That(
      () => MjpegWriter.Create([new() { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("MJPG") }], VideoMetadata.Empty),
      Throws.TypeOf<NotSupportedException>().With.Message.Contains("video only"));

    Assert.That(
      () => MpegVideoWriter.Create([_Video("avc1")], VideoMetadata.Empty),
      Throws.TypeOf<NotSupportedException>().With.Message.Contains("cannot carry"));
  }

  [Test]
  [Category("Unit")]
  public void AFinishedWriterCannotAcceptMorePacketsOrFinishTwice() {
    var writer = MpegVideoWriter.Create([_Video("MPG2")], VideoMetadata.Empty);
    writer.WritePacket(new(0, new byte[] { 1, 2, 3 }));
    Assert.That(writer.Finish(), Is.EqualTo(new byte[] { 1, 2, 3 }));

    Assert.That(() => writer.WritePacket(new(0, new byte[] { 4 })), Throws.TypeOf<InvalidOperationException>());
    Assert.That(() => writer.Finish(), Throws.TypeOf<InvalidOperationException>());
  }
}
