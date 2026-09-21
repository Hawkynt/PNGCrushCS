using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Avi.Tests;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Video.Tests.Formats;

/// <summary>
/// Which AVI packets come back saying a decoder may begin at them.
/// </summary>
/// <remarks>
/// An AVI states this only in its index, and the reader used to read neither index it may carry, so
/// every packet this package demuxed reported that it was not a key frame. Nothing complained,
/// because nothing asked: a codec whose pictures carry their own type reads the same either way. A
/// codec whose pictures do not — ZeroCodec, whose inter picture writes a zero byte to mean "keep the
/// byte under this one" — is told apart by this flag or not at all, and for it the old answer was
/// not merely missing but wrong in a way that produced a picture rather than an error.
/// </remarks>
[TestFixture]
public sealed class AviKeyFrameIndexTests {

  private const int _WIDTH = 32;
  private const int _HEIGHT = 24;

  private static MediaStreamInfo _VideoStream() => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("MJPG"),
    Width = _WIDTH,
    Height = _HEIGHT,
    BitsPerPixel = 24,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static MediaStreamInfo _AudioStream() {
    var waveFormat = new byte[16];
    BinaryPrimitives.WriteUInt16LittleEndian(waveFormat, 1);
    BinaryPrimitives.WriteUInt16LittleEndian(waveFormat.AsSpan(2), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(waveFormat.AsSpan(4), 48_000);
    BinaryPrimitives.WriteUInt32LittleEndian(waveFormat.AsSpan(8), 192_000);
    BinaryPrimitives.WriteUInt16LittleEndian(waveFormat.AsSpan(12), 4);
    BinaryPrimitives.WriteUInt16LittleEndian(waveFormat.AsSpan(14), 16);

    return new() {
      Index = 1,
      Kind = MediaStreamKind.Audio,
      Codec = new CodecTag(1),
      TimeBase = new Rational(1, 48_000),
      CodecPrivateData = waveFormat,
    };
  }

  /// <summary>Which of a video stream's pictures stand alone, as an irregular pattern.</summary>
  /// <remarks>
  /// Irregular on purpose. A run of "every fourth" matches an off-by-one reading of the index as
  /// often as it matches a correct one, and "the first only" is satisfied by a reader that hard-codes
  /// the opening packet. This pattern is satisfied by reading the index.
  /// </remarks>
  private static readonly bool[] _Pattern = [
    true, false, false, true, false, true, true, false, false, false, true, false,
  ];

  private static CodedPacket[] _VideoPackets(int count) {
    var result = new CodedPacket[count];
    for (var i = 0; i < count; ++i)
      result[i] = new(
        0,
        Enumerable.Repeat(checked((byte)(i + 1)), 61 + i % 5).ToArray(),
        IsKeyFrame: _Pattern[i % _Pattern.Length]);

    return result;
  }

  private static IReadOnlyList<bool> _ReadBackFlags(byte[] file, int streamIndex = 0)
    => AviContainer.ReadPackets(AviContainer.FromBytes(file), streamIndex)
      .Select(static packet => packet.IsKeyFrame)
      .ToList();

  [Test]
  [Category("Unit")]
  public void TheLegacyIndexSaysWhichPicturesAreKeyFrames() {
    var packets = _VideoPackets(_Pattern.Length);
    var writer = AviWriter.Create([_VideoStream()], new VideoMetadata());
    foreach (var packet in packets)
      writer.WritePacket(packet);

    Assert.That(_ReadBackFlags(writer.Finish()), Is.EqualTo(_Pattern));
  }

  /// <summary>
  /// A file split into OpenDML segments keeps its flags past the reach of <c>idx1</c>.
  /// </summary>
  /// <remarks>
  /// The legacy index describes the first RIFF and stops. A reader that consulted only it would
  /// answer for the packets of the opening segment and fall back to "not a key frame" for every
  /// packet after them — which looks entirely reasonable and is wrong for most of a long film. The
  /// pattern here puts key frames in the later segments, so reading only <c>idx1</c> fails this.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void TheOpenDmlIndexesSayWhichPicturesAreKeyFramesInEverySegment() {
    var packets = _VideoPackets(24);
    var writer = AviWriter.Create([_VideoStream()], new VideoMetadata());
    foreach (var packet in packets)
      writer.WritePacket(packet);

    // Small enough to force AVIX extensions, which is what puts packets beyond idx1's reach.
    var file = writer.Finish(900);
    var expected = packets.Select(static packet => packet.IsKeyFrame).ToArray();

    Assert.That(_ReadBackFlags(file), Is.EqualTo(expected));
    Assert.That(expected.Skip(_Pattern.Length), Does.Contain(true),
      "The later segments have to carry a key frame or this proves nothing about them.");
  }

  /// <summary>An audio packet is always one a decoder may begin at, and its stream does not shift the video's.</summary>
  [Test]
  [Category("Unit")]
  public void InterleavedAudioDoesNotShiftTheVideoStreamsFlags() {
    var video = _VideoPackets(_Pattern.Length);
    var writer = AviWriter.Create([_VideoStream(), _AudioStream()], new VideoMetadata());
    for (var i = 0; i < video.Length; ++i) {
      writer.WritePacket(video[i]);
      writer.WritePacket(new(1, Enumerable.Repeat((byte)0x55, 32).ToArray(), IsKeyFrame: true));
    }

    var file = writer.Finish();

    Assert.Multiple(() => {
      Assert.That(_ReadBackFlags(file), Is.EqualTo(_Pattern));
      Assert.That(_ReadBackFlags(file, 1), Is.All.True);
    });
  }

  /// <summary>
  /// An AVI that carries no index has said nothing, and nothing is what comes back.
  /// </summary>
  /// <remarks>
  /// The alternative — calling every packet of an unindexed file a key frame — is what turns a
  /// missing statement into a wrong one. A predicted codec handed that answer decodes its inter
  /// pictures as intra ones and produces a plausible wrong picture for every frame of the film,
  /// silently. Refusing to claim leaves a codec that needs the flag to refuse the stream by name.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void AnAviWithoutAnIndexClaimsNoKeyFrames() {
    var file = AviTestContainer.Build(
      "MJPG", _WIDTH, _HEIGHT, 24, [new byte[] { 1, 2, 3, 4 }, new byte[] { 5, 6, 7, 8 }]);

    var flags = _ReadBackFlags(file);

    Assert.Multiple(() => {
      Assert.That(flags, Has.Count.EqualTo(2), "the fixture has to produce packets for this to mean anything");
      Assert.That(flags, Is.All.False);
    });
  }
}
