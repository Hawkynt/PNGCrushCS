using System;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using FileFormat.Yuv4Mpeg;

namespace Hawkynt.FileFormats.Video.Tests.Formats.Yuv4Mpeg;

[TestFixture]
public sealed class Yuv4MpegContainerTests {

  [Test]
  [Category("Unit")]
  public void Handcrafted420StreamDemuxesFramesAndFrameTags() {
    var header = Encoding.ASCII.GetBytes("YUV4MPEG2 W4 H2 F25:1 Ip C420jpeg\n");
    var marker0 = Encoding.ASCII.GetBytes("FRAME Xfoo=bar\n");
    var marker1 = Encoding.ASCII.GetBytes("FRAME\n");
    var frame0 = Enumerable.Range(0, 12).Select(static i => (byte)i).ToArray();
    var frame1 = Enumerable.Range(0, 12).Select(static i => (byte)(0x80 + i)).ToArray();
    var file = header.Concat(marker0).Concat(frame0).Concat(marker1).Concat(frame1).ToArray();

    var container = Yuv4MpegContainer.FromBytes(file);
    var stream = Yuv4MpegContainer.Streams(container).Single();
    Assert.Multiple(() => {
      Assert.That(stream.Width, Is.EqualTo(4));
      Assert.That(stream.Height, Is.EqualTo(2));
      Assert.That(stream.FrameRate, Is.EqualTo(new Rational(25, 1)));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(stream.CodecId, Is.EqualTo("rawvideo"));
      Assert.That(Encoding.ASCII.GetString(stream.CodecPrivateData.Span), Is.EqualTo("420jpeg"));
    });

    var packets = Yuv4MpegContainer.ReadPackets(container).ToArray();
    Assert.That(packets, Has.Length.EqualTo(2));
    Assert.Multiple(() => {
      Assert.That(packets[0].Data.ToArray(), Is.EqualTo(frame0));
      Assert.That(packets[1].Data.ToArray(), Is.EqualTo(frame1));
      Assert.That(packets[0].PresentationTimestamp, Is.EqualTo(0));
      Assert.That(packets[1].PresentationTimestamp, Is.EqualTo(1));
      Assert.That(packets[0].Duration, Is.EqualTo(1));
      Assert.That(packets[0].IsKeyFrame, Is.True);
      Assert.That(Encoding.ASCII.GetString(packets[0].ContainerPrivateData.Span), Is.EqualTo(" Xfoo=bar"));
      Assert.That(packets[1].ContainerPrivateData.IsEmpty, Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void WriterRoundTripPreservesOddWidth422PayloadAndFrameTags() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("YUV "),
      CodecId = "rawvideo",
      Width = 3,
      Height = 2,
      FrameRate = new Rational(30_000, 1001),
      TimeBase = new Rational(1001, 30_000),
      CodecPrivateData = Encoding.ASCII.GetBytes("422"),
    };
    var payload = Enumerable.Range(0, 14).Select(static i => (byte)(i * 7)).ToArray();
    var frameTags = Encoding.ASCII.GetBytes("Xseq=7");

    var file = VideoIO.Mux<Yuv4MpegWriter>(
      [stream],
      [new CodedPacket(0, payload, PresentationTimestamp: 0, DecodeTimestamp: 0, Duration: 1, IsKeyFrame: true, ContainerPrivateData: frameTags)]);

    var textHeader = Encoding.ASCII.GetString(file, 0, Array.IndexOf(file, (byte)'\n') + 1);
    Assert.That(textHeader, Is.EqualTo("YUV4MPEG2 W3 H2 F30000:1001 Ip C422\n"));

    var container = Yuv4MpegContainer.FromBytes(file);
    var roundTrip = Yuv4MpegContainer.Streams(container).Single();
    var packet = Yuv4MpegContainer.ReadPackets(container).Single();
    Assert.Multiple(() => {
      Assert.That(roundTrip.Width, Is.EqualTo(3));
      Assert.That(roundTrip.Height, Is.EqualTo(2));
      Assert.That(roundTrip.FrameRate, Is.EqualTo(new Rational(30_000, 1001)));
      Assert.That(Encoding.ASCII.GetString(roundTrip.CodecPrivateData.Span), Is.EqualTo("422"));
      Assert.That(packet.Data.ToArray(), Is.EqualTo(payload));
      Assert.That(Encoding.ASCII.GetString(packet.ContainerPrivateData.Span), Is.EqualTo(" Xseq=7"));
    });
  }

  [Test]
  [Category("Unit")]
  public void TenBit420UsesTwoBytesPerSample() {
    var header = Encoding.ASCII.GetBytes("YUV4MPEG2 W2 H2 F24:1 Ip C420p10\nFRAME\n");
    var payload = new byte[12];
    var container = Yuv4MpegContainer.FromBytes(header.Concat(payload).ToArray());

    var packet = Yuv4MpegContainer.ReadPackets(container).Single();
    Assert.That(packet.Data.Length, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void TruncatedFrameIsRejectedWhenPacketsAreEnumerated() {
    var file = Encoding.ASCII.GetBytes("YUV4MPEG2 W4 H2 F25:1 Ip C420jpeg\nFRAME\n")
      .Concat(new byte[11])
      .ToArray();
    var container = Yuv4MpegContainer.FromBytes(file);

    Assert.Throws<InvalidDataException>(() => Yuv4MpegContainer.ReadPackets(container).ToArray());
  }

  [Test]
  [Category("Unit")]
  public void WriterRejectsPayloadWithWrongFrameSize() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Width = 4,
      Height = 2,
      FrameRate = new Rational(25, 1),
      CodecPrivateData = Encoding.ASCII.GetBytes("420jpeg"),
    };
    var writer = Yuv4MpegWriter.Create([stream], VideoMetadata.Empty);

    Assert.Throws<InvalidDataException>(() => writer.WritePacket(new CodedPacket(0, new byte[11])));
  }

  [Test]
  [Category("Unit")]
  public void UnsupportedChromaIsRejectedAtHeaderParse() {
    var file = Encoding.ASCII.GetBytes("YUV4MPEG2 W4 H2 F25:1 Ip C420xyz\n");

    Assert.Throws<NotSupportedException>(() => Yuv4MpegContainer.FromBytes(file));
  }
}
