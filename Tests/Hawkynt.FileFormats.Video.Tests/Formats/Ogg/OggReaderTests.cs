using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Ogg.Tests;

/// <summary>
/// The Ogg reader's behaviour: which bitstreams a file declares, where its packets are, and when
/// each of them is due.
/// </summary>
/// <remarks>
/// Everything asserted here was measured first. The packet lists were compared against
/// <c>ffprobe -fflags +noparse</c> on files ffmpeg wrote — the flag matters, because plain ffprobe
/// runs the codec's own parser over the elementary stream and re-splits it into access units, so what
/// it reports stops being what the container holds. Across seven files, of Theora alone, Theora with
/// Vorbis, Theora with Opus, Opus alone and FLAC alone, this reader's packet count, order, sizes and
/// presentation timestamps are identical to ffprobe's for every video packet.
/// <para/>
/// The layouts ffmpeg will not write were built by hand: a page whose lacing ends in a zero, a
/// keyframe and the frames after it sharing a page, a continuation flag with nothing to continue. The
/// reader is a demuxer, so what is tested is packets — how many, how big, whose, and when — and never
/// a picture.
/// </remarks>
[TestFixture]
public sealed class OggReaderTests {

  // ------------------------------------------------------------------------------------------
  // Opening
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => OggReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void WithoutTheCapturePattern_IsRefused() {
    var file = OggTestContainer.Theora();
    file[1] = (byte)'x';

    Assert.That(Assert.Throws<InvalidDataException>(() => OggReader.FromBytes(file))!.Message,
      Does.Contain("OggS"));
  }

  [Test]
  [Category("Unit")]
  public void TooSmallToHoldAPage_IsRefused()
    => Assert.Throws<InvalidDataException>(() => OggReader.FromBytes(new byte[8]));

  [Test]
  [Category("Unit")]
  public void AStructureVersionOtherThanZero_IsRefusedByName() {
    // RFC 3533 defines version zero and no other. A page laid out to rules this reader does not have
    // would still produce packet boundaries, and they would be somebody else's bytes at plausible
    // lengths — which is the failure that cannot be seen from the outside.
    var file = OggTestContainer.Build(
      new OggTestPage { Serial = 1, BeginOfStream = true, Version = 3, Packets = [OggTestContainer.TheoraIdentification()] });

    Assert.That(Assert.Throws<NotSupportedException>(() => OggReader.FromBytes(file))!.Message,
      Does.Contain("version 3").And.Contain("3533"));
  }

  [Test]
  [Category("Unit")]
  public void AFileWithNoBeginOfStreamPage_IsRefused() {
    var file = OggTestContainer.Build(
      new OggTestPage { Serial = 1, Granule = 0, Packets = [OggTestContainer.TheoraIdentification()] });

    Assert.That(Assert.Throws<InvalidDataException>(() => OggReader.FromBytes(file))!.Message,
      Does.Contain("begin-of-stream"));
  }

  [Test]
  [Category("Unit")]
  public void ABitstreamWhoseHeadersAreNotAllThere_IsRefusedByName() {
    // Theora states three header packets and this file holds two. A reader that carried on would
    // report a stream whose setup data is missing, and hand a decoder something that cannot work.
    var file = OggTestContainer.Build(
      new() { Serial = 1, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.TheoraIdentification()] },
      new() { Serial = 1, Sequence = 1, Granule = 0, Packets = [OggTestContainer.TheoraComment()] });

    Assert.That(Assert.Throws<InvalidDataException>(() => OggReader.FromBytes(file))!.Message,
      Does.Contain("header packets").And.Contain("2 of them"));
  }

  // ------------------------------------------------------------------------------------------
  // Checksums
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void TheChecksumIsOggsOwnAndNotAnyOtherCrc32() {
    // A page of one four-byte packet, with the checksum field read as zeroes while the sum is taken.
    // The expected value was computed from RFC 3533 section 6 independently of this library and
    // checked against every page of three files libogg wrote — a stock CRC-32 gives 0x9BE3E0A3 for
    // the same bytes, because Ogg reflects neither the input nor the register, starts at zero rather
    // than at all ones, and does not invert the result.
    byte[] page = [
      0x4F, 0x67, 0x67, 0x53, 0x00, 0x02,
      0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
      0x01, 0x00, 0x00, 0x00,
      0x00, 0x00, 0x00, 0x00,
      0x00, 0x00, 0x00, 0x00,
      0x01,
      0x04,
      0x01, 0x02, 0x03, 0x04,
    ];

    Assert.That(OggCrc.Compute(page), Is.EqualTo(0x52CF423Du));
  }

  [Test]
  [Category("Unit")]
  public void APageWhoseChecksumDoesNotMatchItsBytes_IsRefusedByName() {
    var file = OggTestContainer.Build(
      new() { Serial = 1, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.TheoraIdentification()] },
      new() { Serial = 1, Sequence = 1, Granule = 0, Packets = [OggTestContainer.TheoraComment(), OggTestContainer.TheoraSetup()] },
      new() { Serial = 1, Sequence = 2, Granule = 64, BreakChecksum = true, Packets = [OggTestContainer.TheoraFrame()] });

    var container = OggReader.FromBytes(file);

    Assert.That(Assert.Throws<InvalidDataException>(() => OggContainer.ReadPackets(container).ToList())!.Message,
      Does.Contain("carries checksum").And.Contain("sequence 2"));
  }

  [Test]
  [Category("Unit")]
  public void NoDamagedByteAnywhereProducesTheUndamagedPackets() {
    // Every byte of a page is under its own checksum — the header, the segment table and the body
    // alike — which is what makes a moved packet boundary detectable rather than merely wrong. The
    // property asserted is the one that matters: whatever a damaged file does, it must not quietly
    // hand back what the intact one would.
    //
    // Almost every case is a refusal by name. The exception is a segment table damaged in the file's
    // last page, which then states more body than is there and is indistinguishable from a recording
    // cut off mid-write — there is no whole page to checksum, so the walk ends short instead, and
    // ending short is not the same as being wrong about what it did read.
    var original = OggTestContainer.Theora(3);
    var expected = OggContainer.ReadPackets(OggReader.FromBytes(original)).Select(p => p.Data.Length).ToArray();

    for (var at = 0; at < original.Length; ++at) {
      var damaged = (byte[])original.Clone();
      damaged[at] ^= 0x01;

      int[] produced;
      try {
        produced = OggContainer.ReadPackets(OggReader.FromBytes(damaged)).Select(p => p.Data.Length).ToArray();
      } catch (Exception) {
        continue;
      }

      Assert.That(produced, Is.Not.EqualTo(expected), $"byte {at} was changed and the same packets came out");
    }
  }

  // ------------------------------------------------------------------------------------------
  // Streams
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void ATheoraBitstreamIsDescribedFromItsIdentificationHeader() {
    var container = OggReader.FromBytes(OggTestContainer.Theora());
    var stream = OggContainer.Streams(container)[0];

    Assert.Multiple(() => {
      Assert.That(stream.Index, Is.EqualTo(0));
      Assert.That(stream.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(stream.CodecId, Is.EqualTo("theora"));
      // No four-character code, because there is none anywhere in an Ogg file. Ogg names its codecs
      // with the magic at the head of an identification header, which is text.
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.None));
      Assert.That(stream.Width, Is.EqualTo(176));
      Assert.That(stream.Height, Is.EqualTo(144));
      // One granule unit is one frame, so the time base is the frame rate upside down — which is the
      // 1/25 ffprobe reports for the same file.
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(stream.FrameRate, Is.EqualTo(new Rational(25, 1)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ThePictureRegionIsReportedAndNotTheCodedFrame() {
    // Theora codes whole macroblocks, so a picture 100 by 70 is coded 112 by 80 and cropped. The
    // identification header carries both and ffprobe reports the picture; reporting the frame would
    // give a decoder a size the file never claimed.
    var container = OggReader.FromBytes(OggTestContainer.Theora(1, width: 100, height: 70));
    var stream = OggContainer.Streams(container)[0];

    Assert.That(stream.Width, Is.EqualTo(100));
    Assert.That(stream.Height, Is.EqualTo(70));
  }

  [Test]
  [Category("Unit")]
  public void AnOddFrameRateIsKeptAsAnExactRatio() {
    // 30000/1001 and not 29.97. Rounded, a two-hour film drifts by seconds.
    var file = OggTestContainer.Build(
      new() {
        Serial = 1, Sequence = 0, Granule = 0, BeginOfStream = true,
        Packets = [OggTestContainer.TheoraIdentification(rateNumerator: 30000, rateDenominator: 1001)],
      },
      new() { Serial = 1, Sequence = 1, Granule = 0, Packets = [OggTestContainer.TheoraComment(), OggTestContainer.TheoraSetup()] });

    var stream = OggContainer.Streams(OggReader.FromBytes(file))[0];

    Assert.That(stream.FrameRate, Is.EqualTo(new Rational(30000, 1001)));
    Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1001, 30000)));
  }

  [Test]
  [Category("Unit")]
  public void TheHeaderPacketsCrossAsCodecPrivateDataInXiphLacing() {
    // The three header packets are one block of bytes by the time a decoder sees them, framed the
    // way Matroska frames the same three for the same codecs: a count less one, then the length of
    // every packet but the last, then the packets end to end. Chosen rather than invented so that a
    // Theora decoder reads an Ogg file and a Matroska file with one piece of code.
    var container = OggReader.FromBytes(OggTestContainer.Theora(1));
    var private_ = OggContainer.Streams(container)[0].CodecPrivateData.ToArray();

    var identification = OggTestContainer.TheoraIdentification();
    var comment = OggTestContainer.TheoraComment();
    var setup = OggTestContainer.TheoraSetup();

    Assert.That(private_[0], Is.EqualTo(2), "three packets are stated as two");
    Assert.That(private_[1], Is.EqualTo(identification.Length));
    Assert.That(private_[2], Is.EqualTo(comment.Length));

    var at = 3;
    Assert.That(private_.Skip(at).Take(identification.Length), Is.EqualTo(identification));
    at += identification.Length;
    Assert.That(private_.Skip(at).Take(comment.Length), Is.EqualTo(comment));
    at += comment.Length;
    Assert.That(private_.Skip(at).Take(setup.Length), Is.EqualTo(setup));
    Assert.That(private_.Length, Is.EqualTo(at + setup.Length));
  }

  [Test]
  [Category("Unit")]
  public void ALongHeaderPacketIsLacedAsRunsOfTwoHundredAndFiftyFive() {
    // Xiph lacing states a length as a run of 255s and a remainder, so a setup header of 3196 bytes
    // — which is what libtheora writes — is twelve 255s and a 136.
    var file = OggTestContainer.Build(
      new() { Serial = 1, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.TheoraIdentification()] },
      new() { Serial = 1, Sequence = 1, Granule = 0, Packets = [OggTestContainer.TheoraComment(), OggTestContainer.TheoraSetup(3196)] });

    var private_ = OggContainer.Streams(OggReader.FromBytes(file))[0].CodecPrivateData.ToArray();

    // Only the first two packets get a length; the last is whatever is left.
    Assert.That(private_[0], Is.EqualTo(2));
    Assert.That(private_[1], Is.EqualTo(42));
    Assert.That(private_.Length, Is.EqualTo(1 + 1 + 1 + 42 + OggTestContainer.TheoraComment().Length + 3196));
  }

  // ------------------------------------------------------------------------------------------
  // Packets and timing
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void TheHeaderPacketsAreNotPackets() {
    // A one-second Theora file of twenty-five frames holds twenty-eight packets and ffprobe reports
    // twenty-five. The three headers are the codec's private data rather than coded media, and they
    // are reported once, as that.
    var packets = OggContainer.ReadPackets(OggReader.FromBytes(OggTestContainer.Theora(5))).ToList();

    Assert.That(packets, Has.Count.EqualTo(5));
    Assert.That(packets[0].Data.Length, Is.EqualTo(16), "the first packet is the first frame, not the identification header");
  }

  [Test]
  [Category("Unit")]
  public void AFramesTimestampIsItsGranulePositionUnpackedAndTakenDownByOne() {
    // The granule packs the count of frames up to and including the last keyframe in the high bits
    // and the count since it in the low KFGSHIFT. Both count from one, so the frame's index counting
    // from zero is their sum less one — th_granule_frame in the reference implementation. A file
    // whose first data page carries a granule of 64 with a shift of 6 therefore begins at zero, and
    // that is what ffprobe reports for it.
    var packets = OggContainer.ReadPackets(OggReader.FromBytes(OggTestContainer.Theora(14))).ToList();

    Assert.That(packets.Select(p => p.PresentationTimestamp).ToArray(),
      Is.EqualTo(Enumerable.Range(0, 14).Select(i => (long?)i).ToArray()));

    // Ogg states presentation positions and nothing about decode order; ffprobe reports the two
    // equal for every Ogg packet measured.
    Assert.That(packets.Select(p => p.DecodeTimestamp), Is.EqualTo(packets.Select(p => p.PresentationTimestamp)));

    // One frame each, in a time base of one frame period.
    Assert.That(packets.Select(p => p.Duration).Distinct().ToArray(), Is.EqualTo(new long?[] { 1 }));
  }

  [Test]
  [Category("Unit")]
  public void FramesSharingAPageAreCountedBackFromTheOneThatEndsIt() {
    // Only the last packet finishing on a page has a position; ffmpeg's muxer puts eleven frames on
    // one page routinely. Where a mapping advances one unit a packet — which Theora does and no
    // other mapping here does — the rest are counted back from it exactly.
    var file = OggTestContainer.Build(
      new() { Serial = 1, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.TheoraIdentification()] },
      new() { Serial = 1, Sequence = 1, Granule = 0, Packets = [OggTestContainer.TheoraComment(), OggTestContainer.TheoraSetup()] },
      new() { Serial = 1, Sequence = 2, Granule = 1 << OggTestContainer.THEORA_GRANULE_SHIFT, Packets = [OggTestContainer.TheoraFrame(20)] },
      new() {
        Serial = 1, Sequence = 3, Granule = (1L << OggTestContainer.THEORA_GRANULE_SHIFT) | 4, EndOfStream = true,
        Packets = [
          OggTestContainer.TheoraFrame(21, false),
          OggTestContainer.TheoraFrame(22, false),
          OggTestContainer.TheoraFrame(23, false),
          OggTestContainer.TheoraFrame(24, false),
        ],
      });

    var packets = OggContainer.ReadPackets(OggReader.FromBytes(file)).ToList();

    Assert.That(packets.Select(p => p.PresentationTimestamp).ToArray(), Is.EqualTo(new long?[] { 0, 1, 2, 3, 4 }));
    Assert.That(packets.Select(p => p.Data.Length).ToArray(), Is.EqualTo(new[] { 20, 21, 22, 23, 24 }));
  }

  [Test]
  [Category("Unit")]
  public void AKeyFrameIsToldByTheFrameTypeBitAndNotByItsPosition() {
    // The high bit of a data packet's first byte says it is data and the next bit is the frame type,
    // clear for intra. Read from the packet rather than inferred from the granule, because a page
    // holding several frames states one position for the last of them and nothing for the rest.
    var packets = OggContainer.ReadPackets(OggReader.FromBytes(OggTestContainer.Theora(14))).ToList();

    Assert.That(packets.Select(p => p.IsKeyFrame).ToArray(),
      Is.EqualTo(new[] { true, false, false, false, false, false, false, false, false, false, false, false, true, false }));
  }

  [Test]
  [Category("Unit")]
  public void AZeroLengthPacketIsAPacket() {
    // Theora's way of saying "show the previous frame again". It occupies a lacing value of zero and
    // nothing else, and a reader that treated an empty segment as padding would lose the frame and
    // put every timestamp after it one out. ffprobe reports it as a packet of size zero.
    var file = OggTestContainer.Build(
      new() { Serial = 1, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.TheoraIdentification()] },
      new() { Serial = 1, Sequence = 1, Granule = 0, Packets = [OggTestContainer.TheoraComment(), OggTestContainer.TheoraSetup()] },
      new() { Serial = 1, Sequence = 2, Granule = 1 << OggTestContainer.THEORA_GRANULE_SHIFT, Packets = [OggTestContainer.TheoraFrame(20)] },
      new() {
        Serial = 1, Sequence = 3, Granule = (1L << OggTestContainer.THEORA_GRANULE_SHIFT) | 3, EndOfStream = true,
        Packets = [[], [], OggTestContainer.TheoraFrame(9, false)],
      });

    var packets = OggContainer.ReadPackets(OggReader.FromBytes(file)).ToList();

    Assert.That(packets.Select(p => p.Data.Length).ToArray(), Is.EqualTo(new[] { 20, 0, 0, 9 }));
    Assert.That(packets.Select(p => p.PresentationTimestamp).ToArray(), Is.EqualTo(new long?[] { 0, 1, 2, 3 }));
    Assert.That(packets.Skip(1).Select(p => p.IsKeyFrame).ToArray(), Is.EqualTo(new[] { false, false, false }));
  }

  [Test]
  [Category("Unit")]
  public void APacketWhoseLengthDividesByTwoHundredAndFiftyFiveEndsOnAZeroSegment() {
    // The one thing that ends a packet is a lacing value under 255, so a packet of exactly 510 bytes
    // is written 255, 255, 0. A reader that ended packets on a non-empty segment would weld this one
    // to the next.
    var frame = OggTestContainer.TheoraFrame(510);
    var file = OggTestContainer.Build(
      new() { Serial = 1, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.TheoraIdentification()] },
      new() { Serial = 1, Sequence = 1, Granule = 0, Packets = [OggTestContainer.TheoraComment(), OggTestContainer.TheoraSetup()] },
      new() {
        Serial = 1, Sequence = 2, Granule = (1L << OggTestContainer.THEORA_GRANULE_SHIFT) | 1, EndOfStream = true,
        Packets = [frame, OggTestContainer.TheoraFrame(7, false)],
      });

    var packets = OggContainer.ReadPackets(OggReader.FromBytes(file)).ToList();

    Assert.That(packets.Select(p => p.Data.Length).ToArray(), Is.EqualTo(new[] { 510, 7 }));
    Assert.That(packets[0].Data.ToArray(), Is.EqualTo(frame));
  }

  [TestCase(2)]
  [TestCase(3)]
  [Category("Unit")]
  public void APacketLargerThanAPageIsPutBackTogether(int pages) {
    // A page holds at most 255 segments of 255 bytes, so nothing over 65 025 bytes fits in one — and
    // every Theora keyframe of a large picture is over it. ffmpeg writes exactly this: a page of 255
    // full segments with a granule of -1, then a page with the continuation flag set.
    const int FULL_PAGE = 255 * 255;
    var frame = OggTestContainer.TheoraFrame(FULL_PAGE * (pages - 1) + 1000);

    var built = new List<OggTestPage> {
      new() { Serial = 1, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.TheoraIdentification()] },
      new() { Serial = 1, Sequence = 1, Granule = 0, Packets = [OggTestContainer.TheoraComment(), OggTestContainer.TheoraSetup()] },
    };

    for (var i = 0; i < pages - 1; ++i)
      built.Add(new() {
        Serial = 1,
        Sequence = (uint)(2 + i),
        // No packet finishes on this page, which is the whole of what -1 means.
        Granule = -1,
        Continued = i > 0,
        Tail = frame.Skip(i * FULL_PAGE).Take(FULL_PAGE).ToArray(),
      });

    built.Add(new() {
      Serial = 1,
      Sequence = (uint)(1 + pages),
      Granule = 1 << OggTestContainer.THEORA_GRANULE_SHIFT,
      Continued = true,
      EndOfStream = true,
      Packets = [frame.Skip((pages - 1) * FULL_PAGE).ToArray()],
    });

    var packets = OggContainer.ReadPackets(OggReader.FromBytes(OggTestContainer.Build(built.ToArray()))).ToList();

    Assert.That(packets, Has.Count.EqualTo(1));
    Assert.That(packets[0].Data.Length, Is.EqualTo(frame.Length));
    Assert.That(packets[0].Data.ToArray(), Is.EqualTo(frame));
    Assert.That(packets[0].PresentationTimestamp, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AContinuationWithNothingToContinueIsDropped() {
    // A page claiming to continue a packet whose head is not in the file — which is what the middle
    // of a stream looks like to something that started reading there. The fragment is dropped rather
    // than handed on, because the back half of a frame decodes to noise and noise is worse than a
    // frame that is not there.
    var file = OggTestContainer.Build(
      new() { Serial = 1, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.TheoraIdentification()] },
      new() { Serial = 1, Sequence = 1, Granule = 0, Packets = [OggTestContainer.TheoraComment(), OggTestContainer.TheoraSetup()] },
      new() {
        Serial = 1, Sequence = 2, Granule = 1 << OggTestContainer.THEORA_GRANULE_SHIFT, Continued = true, EndOfStream = true,
        Packets = [OggTestContainer.TheoraFrame(30), OggTestContainer.TheoraFrame(40, false)],
      });

    var packets = OggContainer.ReadPackets(OggReader.FromBytes(file)).ToList();

    Assert.That(packets.Select(p => p.Data.Length).ToArray(), Is.EqualTo(new[] { 40 }));
  }

  [Test]
  [Category("Unit")]
  public void TheWalkIsLazyAndCanBeRunTwice() {
    // A film is not a list of packets held in memory. Nothing of a page is touched until a packet is
    // asked for, and asking again walks the file again rather than replaying a cached list.
    var container = OggReader.FromBytes(OggTestContainer.Theora(6));
    var walk = OggContainer.ReadPackets(container);

    Assert.That(walk.Count(), Is.EqualTo(6));
    Assert.That(walk.Count(), Is.EqualTo(6));
    Assert.That(walk.First().Data.Length, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void APacketOnOnePageIsAWindowOntoTheFileRatherThanACopy() {
    // A demuxer walking a film must not leave a copy of it behind. The only packets copied are the
    // ones that span pages, whose halves are separated in the file by another page's header.
    var file = OggTestContainer.Theora(1);
    var container = OggReader.FromBytes(file);
    var packet = OggContainer.ReadPackets(container).Single();

    Assert.That(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(packet.Data, out var segment), Is.True);
    Assert.That(segment.Array, Is.SameAs(file));
  }

  // ------------------------------------------------------------------------------------------
  // Several bitstreams at once
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void TwoBitstreamsAreReportedInTheOrderTheyDeclareThemselves() {
    var container = OggReader.FromBytes(_TheoraAndVorbis());
    var streams = OggContainer.Streams(container);

    Assert.Multiple(() => {
      Assert.That(streams, Has.Count.EqualTo(2));
      Assert.That(streams[0].CodecId, Is.EqualTo("theora"));
      Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(streams[1].CodecId, Is.EqualTo("vorbis"));
      Assert.That(streams[1].Kind, Is.EqualTo(MediaStreamKind.Audio));
      Assert.That(streams[1].TimeBase, Is.EqualTo(new Rational(1, 44100)));
    });
  }

  [Test]
  [Category("Unit")]
  public void PacketsComeOutInStorageOrderAndNotOneStreamAfterAnother() {
    // Storage order is the interleaving the writer chose, so that a player reading forwards has both
    // streams by the time it needs them. Nothing is merged to recover it — the pages are already in
    // that order.
    var packets = OggContainer.ReadPackets(OggReader.FromBytes(_TheoraAndVorbis())).ToList();

    Assert.That(packets.Select(p => p.StreamIndex).ToArray(), Is.EqualTo(new[] { 0, 0, 1, 1, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void OneStreamsPacketsCanBeWalkedOnTheirOwn() {
    var container = OggReader.FromBytes(_TheoraAndVorbis());

    Assert.That(OggContainer.ReadPackets(container, 0).Select(p => p.StreamIndex).ToArray(), Is.EqualTo(new[] { 0, 0, 0 }));
    Assert.That(OggContainer.ReadPackets(container, 1).Select(p => p.StreamIndex).ToArray(), Is.EqualTo(new[] { 1, 1 }));
    Assert.That(OggContainer.ReadPackets(container, 2), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void OneStreamsTimingIsNotDisturbedByTheOther() {
    // Each bitstream carries its own granule positions and its own pending fragment; sharing either
    // between them is the mistake that makes a film with sound time correctly and a film with sound
    // and subtitles not.
    var packets = OggContainer.ReadPackets(OggReader.FromBytes(_TheoraAndVorbis()), 0).ToList();

    Assert.That(packets.Select(p => p.PresentationTimestamp).ToArray(), Is.EqualTo(new long?[] { 0, 1, 2 }));
  }

  // ------------------------------------------------------------------------------------------
  // The other mappings
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void AnOpusBitstreamIsIdentifiedAndItsPreSkipTakenOff() {
    // RFC 7845 section 4: granule positions are 48 kHz samples whatever the encoder was fed, and the
    // first playable sample is at the granule less the pre-skip. So a file whose header pages state
    // a position of zero begins at minus the pre-skip, which is the -312 ffprobe reports for a file
    // ffmpeg wrote with a pre-skip of 312.
    var file = OggTestContainer.Build(
      new() { Serial = 9, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.OpusHead()] },
      new() { Serial = 9, Sequence = 1, Granule = 0, Packets = [OggTestContainer.OpusTags()] },
      new() { Serial = 9, Sequence = 2, Granule = 48000, EndOfStream = true, Packets = [OggTestContainer.UnknownPacket(50), OggTestContainer.UnknownPacket(60)] });

    var container = OggReader.FromBytes(file);
    var stream = OggContainer.Streams(container)[0];
    var packets = OggContainer.ReadPackets(container).ToList();

    Assert.Multiple(() => {
      Assert.That(stream.CodecId, Is.EqualTo("opus"));
      Assert.That(stream.Kind, Is.EqualTo(MediaStreamKind.Audio));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 48000)));
      Assert.That(packets.Select(p => p.Data.Length).ToArray(), Is.EqualTo(new[] { 50, 60 }));
      // The first data packet begins where the header pages left the position, less the pre-skip.
      Assert.That(packets[0].PresentationTimestamp, Is.EqualTo(-312));
      // And the second states nothing, because the file states nothing: a granule position sits at
      // the end of a page and belongs to the last packet finishing on it. Working backwards from it
      // would take the block sizes out of the codec's own setup data, which is decoding.
      Assert.That(packets[1].PresentationTimestamp, Is.Null);
      Assert.That(packets.Select(p => p.Duration).Distinct().ToArray(), Is.EqualTo(new long?[] { null }));
    });
  }

  [Test]
  [Category("Unit")]
  public void ThePositionAPageStatesBecomesTheTimestampOfThePacketThatBeginsAtIt() {
    // The only per-packet position an audio mapping's file states. It was checked against ffprobe on
    // a file ffmpeg wrote: the Opus page stating 48 000 is followed by a packet ffprobe times at
    // 47 688, which is that position less the 312-sample pre-skip.
    var file = OggTestContainer.Build(
      new() { Serial = 9, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.OpusHead()] },
      new() { Serial = 9, Sequence = 1, Granule = 0, Packets = [OggTestContainer.OpusTags()] },
      new() { Serial = 9, Sequence = 2, Granule = 48000, Packets = [OggTestContainer.UnknownPacket(50), OggTestContainer.UnknownPacket(60)] },
      new() { Serial = 9, Sequence = 3, Granule = 48960, EndOfStream = true, Packets = [OggTestContainer.UnknownPacket(70)] });

    var packets = OggContainer.ReadPackets(OggReader.FromBytes(file)).ToList();

    Assert.That(packets.Select(p => p.PresentationTimestamp).ToArray(), Is.EqualTo(new long?[] { -312, null, 47688 }));
  }

  [Test]
  [Category("Unit")]
  public void AFlacBitstreamStatesHowManyHeadersItHas() {
    var file = OggTestContainer.Build(
      new() { Serial = 4, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.FlacMapping()] },
      new() { Serial = 4, Sequence = 1, Granule = 0, Packets = [OggTestContainer.FlacMetadataBlock()] },
      new() { Serial = 4, Sequence = 2, Granule = 4096, EndOfStream = true, Packets = [OggTestContainer.FlacFrame(30)] });

    var container = OggReader.FromBytes(file);
    var stream = OggContainer.Streams(container)[0];

    Assert.That(stream.CodecId, Is.EqualTo("flac"));
    Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 44100)));
    Assert.That(OggContainer.ReadPackets(container).Select(p => p.Data.Length).ToArray(), Is.EqualTo(new[] { 30 }));
  }

  [Test]
  [Category("Unit")]
  public void AFlacBitstreamThatDoesNotKnowIsReadFromItsLastBlockFlag() {
    // The mapping allows a writer that did not know the count to state zero. The metadata blocks then
    // run until one says it is the last, in the high bit of its own first byte — which is the only
    // way to tell, because a block's type field cannot be told from an audio frame's 0xFF sync.
    var file = OggTestContainer.Build(
      new() { Serial = 4, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.FlacMapping(followingHeaders: 0)] },
      new() { Serial = 4, Sequence = 1, Granule = 0, Packets = [OggTestContainer.FlacMetadataBlock(4, false), OggTestContainer.FlacMetadataBlock(6, true)] },
      new() { Serial = 4, Sequence = 2, Granule = 4096, EndOfStream = true, Packets = [OggTestContainer.FlacFrame(30), OggTestContainer.FlacFrame(31)] });

    var packets = OggContainer.ReadPackets(OggReader.FromBytes(file)).ToList();

    Assert.That(packets.Select(p => p.Data.Length).ToArray(), Is.EqualTo(new[] { 30, 31 }));
  }

  [Test]
  [Category("Unit")]
  public void ABitstreamOfAMappingNothingHereKnowsStillDemuxes() {
    // The point of the demux-and-decode split. A file holding a codec nothing here reads is a
    // perfectly good file, and copying its packets into another container needs no decoder at all —
    // so it is reported, its packets come out whole and in order, and it carries no timing, because
    // nothing here knows what its granule counts.
    var file = OggTestContainer.Build(
      new() { Serial = 7, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.UnknownPacket(20, 0x11)] },
      new() { Serial = 7, Sequence = 1, Granule = 5, EndOfStream = true, Packets = [OggTestContainer.UnknownPacket(21, 0x22), OggTestContainer.UnknownPacket(22, 0x33)] });

    var container = OggReader.FromBytes(file);
    var stream = OggContainer.Streams(container)[0];
    var packets = OggContainer.ReadPackets(container).ToList();

    Assert.Multiple(() => {
      Assert.That(stream.Kind, Is.EqualTo(MediaStreamKind.Unknown));
      Assert.That(stream.CodecId, Is.Null);
      Assert.That(stream.TimeBase, Is.EqualTo(Rational.Unknown));
      Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(0), "nothing here knows which of its packets are headers");
      // Every packet, the first one included — a reader that guessed at a header count would hide a
      // packet a muxer copying the bitstream across would need.
      Assert.That(packets.Select(p => p.Data.Length).ToArray(), Is.EqualTo(new[] { 20, 21, 22 }));
      Assert.That(packets.Select(p => p.PresentationTimestamp).Distinct().ToArray(), Is.EqualTo(new long?[] { null }));
      Assert.That(packets.Select(p => p.IsKeyFrame).Distinct().ToArray(), Is.EqualTo(new[] { false }));
    });
  }

  [Test]
  [Category("Unit")]
  public void ABitstreamDeclaredAfterOneWithNoHeadersIsStillFound() {
    // The scan stops when every declared bitstream has its headers, and a mapping nothing here knows
    // has none — so it finishes on its own begin-of-stream page. Stopping there would stop before
    // the begin-of-stream page of whatever is multiplexed after it, and the file would come back
    // with half its streams. RFC 3533 puts all of them ahead of every other page, so the scan runs
    // to the first page that is not one.
    var file = OggTestContainer.Build(
      new() { Serial = 7, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.UnknownPacket(20, 0x11)] },
      new() { Serial = 1, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.TheoraIdentification()] },
      new() { Serial = 1, Sequence = 1, Granule = 0, Packets = [OggTestContainer.TheoraComment(), OggTestContainer.TheoraSetup()] },
      new() { Serial = 1, Sequence = 2, Granule = 1 << OggTestContainer.THEORA_GRANULE_SHIFT, EndOfStream = true, Packets = [OggTestContainer.TheoraFrame(30)] },
      new() { Serial = 7, Sequence = 1, Granule = 5, EndOfStream = true, Packets = [OggTestContainer.UnknownPacket(21, 0x22)] });

    var container = OggReader.FromBytes(file);
    var streams = OggContainer.Streams(container);

    Assert.That(streams.Select(s => s.CodecId).ToArray(), Is.EqualTo(new string?[] { null, "theora" }));
    Assert.That(OggContainer.ReadPackets(container).Select(p => (p.StreamIndex, p.Data.Length)).ToArray(),
      Is.EqualTo(new[] { (0, 20), (1, 30), (0, 21) }));
  }

  // ------------------------------------------------------------------------------------------
  // Metadata
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void TheCommentHeaderIsReadForWhatTheFileSaysAboutItself() {
    var file = OggTestContainer.Build(
      new() { Serial = 1, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.TheoraIdentification()] },
      new() {
        Serial = 1, Sequence = 1, Granule = 0,
        Packets = [OggTestContainer.TheoraComment("Lavf60", "TITLE=A Film", "ARTIST=Nobody", "COMMENT=Nothing"), OggTestContainer.TheoraSetup()],
      });

    var metadata = OggContainer.Metadata(OggReader.FromBytes(file));

    Assert.Multiple(() => {
      Assert.That(metadata.Title, Is.EqualTo("A Film"));
      Assert.That(metadata.Artist, Is.EqualTo("Nobody"));
      Assert.That(metadata.EncodedBy, Is.EqualTo("Lavf60"));
      Assert.That(metadata.TextEntries.Select(e => e.Keyword), Does.Contain("COMMENT"));
      Assert.That(metadata.Streams, Has.Count.EqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheCommentHeadersDateIsReadAsTheCreationTime() {
    var file = _WithComment("DATE=2019-04-02T11:30:00+02:00");

    Assert.That(OggContainer.Metadata(OggReader.FromBytes(file)).CreationTime,
      Is.EqualTo(new DateTimeOffset(2019, 4, 2, 11, 30, 0, TimeSpan.FromHours(2))));
  }

  [Test]
  [Category("Unit")]
  public void ADateFieldStatingSomethingElseStaysTheAnnotationItAlsoIs() {
    // Vorbis's DATE field is defined as ISO 8601 and files put other things in it. What cannot be
    // read as an instant is not turned into one; it is still reported under its own name.
    var file = _WithComment("DATE=whenever it was");

    var metadata = OggContainer.Metadata(OggReader.FromBytes(file));

    Assert.Multiple(() => {
      Assert.That(metadata.CreationTime, Is.Null);
      Assert.That(metadata.TextEntries.Select(e => (e.Keyword, e.Text)), Does.Contain(("DATE", "whenever it was")));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADateWithNoTimeOfDayIsReadTheSameWhereverItIsOpened() {
    var file = _WithComment("DATE=2019-04-02");

    Assert.That(OggContainer.Metadata(OggReader.FromBytes(file)).CreationTime,
      Is.EqualTo(new DateTimeOffset(2019, 4, 2, 0, 0, 0, TimeSpan.Zero)));
  }

  private static byte[] _WithComment(params string[] tags) => OggTestContainer.Build(
    new() { Serial = 1, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.TheoraIdentification()] },
    new() { Serial = 1, Sequence = 1, Granule = 0, Packets = [OggTestContainer.TheoraComment("Lavf60", tags), OggTestContainer.TheoraSetup()] });

  // ------------------------------------------------------------------------------------------
  // The registry
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void AnOggFileIsDetectedByItsBytes()
    => Assert.That(VideoFormatRegistry.Detect(OggTestContainer.Theora(1)), Is.EqualTo(VideoFormat.Ogg));

  [Test]
  [Category("Unit")]
  public void EveryNameTheOneContainerGoesUnderReachesIt() {
    foreach (var extension in new[] { ".ogg", ".ogv", ".oga", ".ogx", ".opus", ".spx" })
      Assert.That(VideoFormatRegistry.ByExtension(extension), Does.Contain(VideoFormat.Ogg), extension);

    Assert.That(VideoFormatRegistry.ByMimeType("video/ogg"), Is.EqualTo(VideoFormat.Ogg));
    Assert.That(VideoFormatRegistry.ByMimeType("audio/ogg"), Is.EqualTo(VideoFormat.Ogg));
    Assert.That(VideoFormatRegistry.ByMimeType("application/ogg"), Is.EqualTo(VideoFormat.Ogg));
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryWalksAnOggFilesPacketsWithoutBeingToldWhatItIs()
    => Assert.That(VideoFormatRegistry.ReadPackets(OggTestContainer.Theora(4)).Count(), Is.EqualTo(4));

  // ------------------------------------------------------------------------------------------
  // Helpers
  // ------------------------------------------------------------------------------------------

  /// <summary>
  /// A file of pictures and sound, interleaved the way ffmpeg interleaves them.
  /// </summary>
  /// <remarks>
  /// Both begin-of-stream pages first and in declaration order, which RFC 3533 requires; then the
  /// rest of each bitstream's headers; then the media pages in the order a player needs them.
  /// </remarks>
  private static byte[] _TheoraAndVorbis()
    => OggTestContainer.Build(
      new() { Serial = 1, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.TheoraIdentification()] },
      new() { Serial = 2, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.VorbisIdentification()] },
      new() { Serial = 1, Sequence = 1, Granule = 0, Packets = [OggTestContainer.TheoraComment(), OggTestContainer.TheoraSetup()] },
      new() { Serial = 2, Sequence = 1, Granule = 0, Packets = [OggTestContainer.VorbisComment(), OggTestContainer.VorbisSetup()] },
      new() { Serial = 1, Sequence = 2, Granule = 1 << OggTestContainer.THEORA_GRANULE_SHIFT, Packets = [OggTestContainer.TheoraFrame(30)] },
      new() { Serial = 1, Sequence = 3, Granule = (1L << OggTestContainer.THEORA_GRANULE_SHIFT) | 1, Packets = [OggTestContainer.TheoraFrame(31, false)] },
      new() { Serial = 2, Sequence = 2, Granule = 2048, Packets = [OggTestContainer.UnknownPacket(40), OggTestContainer.UnknownPacket(41)] },
      new() { Serial = 1, Sequence = 4, Granule = (1L << OggTestContainer.THEORA_GRANULE_SHIFT) | 2, EndOfStream = true, Packets = [OggTestContainer.TheoraFrame(32, false)] });
}
