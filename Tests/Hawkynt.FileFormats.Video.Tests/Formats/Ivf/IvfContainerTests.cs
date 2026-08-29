using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.Ivf;

namespace Hawkynt.FileFormats.Video.Tests.Formats.Ivf;

[TestFixture]
public sealed class IvfContainerTests {

  [Test]
  [Category("Unit")]
  public void HandcraftedVp9FileDemuxesHeaderTimingAndPackets() {
    var file = new byte[32 + 12 + 3 + 12 + 2];
    "DKIF"u8.CopyTo(file);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(6), 32);
    "VP90"u8.CopyTo(file.AsSpan(8));
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(12), 16);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(14), 8);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(16), 30);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(20), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(24), 2);

    var at = 32;
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at), 3);
    BinaryPrimitives.WriteInt64LittleEndian(file.AsSpan(at + 4), 7);
    new byte[] { 1, 2, 3 }.CopyTo(file, at + 12);
    at += 15;
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at), 2);
    BinaryPrimitives.WriteInt64LittleEndian(file.AsSpan(at + 4), 10);
    new byte[] { 4, 5 }.CopyTo(file, at + 12);

    var container = IvfContainer.FromBytes(file);
    var stream = IvfContainer.Streams(container).Single();
    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("VP90")));
      Assert.That(stream.Width, Is.EqualTo(16));
      Assert.That(stream.Height, Is.EqualTo(8));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 30)));
      Assert.That(stream.FrameRate, Is.EqualTo(new Rational(30, 1)));
      Assert.That(stream.DeclaredFrameCount, Is.EqualTo(2));
    });

    var packets = IvfContainer.ReadPackets(container).ToArray();
    Assert.Multiple(() => {
      Assert.That(packets, Has.Length.EqualTo(2));
      Assert.That(packets[0].PresentationTimestamp, Is.EqualTo(7));
      Assert.That(packets[0].Data.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
      Assert.That(packets[1].PresentationTimestamp, Is.EqualTo(10));
      Assert.That(packets[1].Data.ToArray(), Is.EqualTo(new byte[] { 4, 5 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void Av1MuxRoundTripWritesFrameCountAndKeepsTimestamps() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("AV01"),
      Width = 320,
      Height = 180,
      TimeBase = new Rational(1, 1000),
      FrameRate = new Rational(25, 1),
    };
    var first = new byte[] { 0x12, 0x34, 0x56 };
    var second = new byte[] { 0xAB, 0xCD };

    var file = VideoIO.Mux<IvfWriter>(
      [stream],
      [
        new CodedPacket(0, first, PresentationTimestamp: 0, DecodeTimestamp: 0, IsKeyFrame: true),
        new CodedPacket(0, second, PresentationTimestamp: 40, DecodeTimestamp: 40),
      ]);

    Assert.Multiple(() => {
      Assert.That(file.AsSpan(0, 4).ToArray(), Is.EqualTo("DKIF"u8.ToArray()));
      Assert.That(file.AsSpan(8, 4).ToArray(), Is.EqualTo("AV01"u8.ToArray()));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(16)), Is.EqualTo(1000));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(20)), Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(24)), Is.EqualTo(2));
    });

    var container = IvfContainer.FromBytes(file);
    var packets = IvfContainer.ReadPackets(container).ToArray();
    Assert.Multiple(() => {
      Assert.That(packets.Select(p => p.Data.ToArray()), Is.EqualTo(new[] { first, second }));
      Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 0, 40 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void TruncatedFramePayloadIsRejectedDuringPacketWalk() {
    var file = new byte[32 + 12 + 2];
    "DKIF"u8.CopyTo(file);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(6), 32);
    "VP80"u8.CopyTo(file.AsSpan(8));
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(12), 16);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(14), 16);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(16), 30);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(20), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(24), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(32), 10);

    var container = IvfContainer.FromBytes(file);
    Assert.Throws<InvalidDataException>(() => IvfContainer.ReadPackets(container).ToArray());
  }
}
