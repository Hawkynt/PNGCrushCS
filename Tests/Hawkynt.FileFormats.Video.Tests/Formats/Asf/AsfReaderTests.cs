using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Asf.Tests;

/// <summary>
/// The ASF reader's behaviour — Advanced Systems Format being one format under the three extensions
/// <c>.asf</c>, <c>.wmv</c> and <c>.wma</c>.
/// </summary>
/// <remarks>
/// Everything asserted here was first measured against ffprobe, on files ffmpeg wrote where ffmpeg
/// writes them and on files assembled by hand where it does not. The measurement was taken with
/// <c>-fflags +noparse</c> throughout: without it ffprobe runs the codec's own parser over the
/// elementary stream and re-splits it into access units, so the packet sizes it reports are the
/// parser's and not the container's, and it interpolates timestamps a file never carried.
/// <para/>
/// ffmpeg's ASF muxer writes exactly one shape — error correction present, several payloads to a
/// packet, each stating its own length. A packet with no error correction block, a packet stating its
/// own length, a single-payload packet, a compressed payload carrying several whole objects at once
/// and a <c>WM/Picture</c> are all legal and none of them appear in its output, so each was assembled
/// by hand here, written out, put past ffprobe, and only written down as a test once ffprobe read the
/// same packets out of it.
/// <para/>
/// The reader is a demuxer and nothing else, so most of what is tested is packets: how many, how big,
/// which stream, and when each is due.
/// </remarks>
[TestFixture]
public sealed class AsfReaderTests {

  // ------------------------------------------------------------------------------------------
  // Opening
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AsfReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => AsfReader.FromBytes(new byte[12]));

  [Test]
  [Category("Unit")]
  public void WithoutTheHeaderObjectsIdentifier_IsRefusedByName() {
    var file = _Simple();
    file[0] = 0x31;

    var refusal = Assert.Throws<InvalidDataException>(() => AsfReader.FromBytes(file));

    // The refusal names what it found, because sixteen bytes of hexadecimal identify nothing to
    // anybody and the whole format is keyed by these identifiers.
    Assert.That(refusal!.Message, Does.Contain("31"));
  }

  [Test]
  [Category("Unit")]
  public void WithoutFileProperties_IsRefusedByName() {
    // Not for tidiness: the object states the packet size, and every packet that states no length of
    // its own is that many bytes long. Without it the Data Object cannot be walked at all.
    var file = AsfTestContainer.Build([new AsfTestStream()], withoutFileProperties: true);

    var refusal = Assert.Throws<InvalidDataException>(() => AsfReader.FromBytes(file));

    Assert.That(refusal!.Message, Does.Contain("File Properties"));
  }

  [Test]
  [Category("Unit")]
  public void WithoutADataObject_IsRefusedByName() {
    var file = AsfTestContainer.Build([new AsfTestStream()], withoutDataObject: true);

    var refusal = Assert.Throws<InvalidDataException>(() => AsfReader.FromBytes(file));

    Assert.That(refusal!.Message, Does.Contain("Data Object"));
  }

  [Test]
  [Category("Unit")]
  public void APacketSizeOfZero_IsRefused() {
    // Every packet in the file would be nought bytes long, which is a walk that never advances.
    var file = AsfTestContainer.Build([new AsfTestStream()], packetSize: 0);

    Assert.Throws<InvalidDataException>(() => AsfReader.FromBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void ReadingFromASpanAndFromAnArrayAgree() {
    var file = _Simple();

    var fromArray = AsfContainer.ReadPackets(AsfReader.FromBytes(file)).Select(p => p.Data.Length).ToList();
    var fromSpan = AsfContainer.ReadPackets(AsfReader.FromSpan(file)).Select(p => p.Data.Length).ToList();

    Assert.That(fromSpan, Is.EqualTo(fromArray));
  }

  [Test]
  [Category("Unit")]
  public void ReadingFromAFileAndFromAnArrayAgree() {
    var path = Path.Combine(Path.GetTempPath(), $"asf-{Guid.NewGuid():N}.wmv");
    var file = _Simple();

    try {
      File.WriteAllBytes(path, file);

      var fromDisc = AsfContainer.ReadPackets(AsfContainer.FromFile(new(path))).Select(p => p.Data.Length).ToList();
      var fromArray = AsfContainer.ReadPackets(AsfReader.FromBytes(file)).Select(p => p.Data.Length).ToList();

      Assert.That(fromDisc, Is.EqualTo(fromArray));
    } finally {
      File.Delete(path);
    }
  }

  [Test]
  [Category("Unit")]
  public void AMissingFileIsRefused()
    => Assert.Throws<FileNotFoundException>(
      () => AsfContainer.FromFile(new(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.wmv"))));

  // ------------------------------------------------------------------------------------------
  // Streams
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void AVideoStreamIsDescribedFromItsFormatData() {
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build([new AsfTestStream { FourCharacterCode = "WMV3", Width = 176, Height = 144, BitsPerPixel = 24 }]));

    var stream = AsfContainer.Streams(container)[0];

    Assert.Multiple(() => {
      Assert.That(stream.Index, Is.EqualTo(0));
      Assert.That(stream.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("WMV3")));
      Assert.That(stream.Width, Is.EqualTo(176));
      Assert.That(stream.Height, Is.EqualTo(144));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(24));

      // Milliseconds, for every stream of every ASF file — the format has no per-stream clock at all.
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 1000)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsNamedByItsFormatTag() {
    // Reading the first field of a WAVEFORMATEX is not decoding sound; it is what lets a muxer copy
    // the stream across intact, and it is what the video branch does with biCompression.
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build([new AsfTestStream { Media = AsfTestMedia.Audio, FormatTag = 0x0161 }]));

    var stream = AsfContainer.Streams(container)[0];

    Assert.Multiple(() => {
      Assert.That(stream.Kind, Is.EqualTo(MediaStreamKind.Audio));
      Assert.That(stream.Codec, Is.EqualTo(new CodecTag(0x0161)));
      Assert.That(stream.Width, Is.Zero);
    });
  }

  [TestCase(AsfTestMedia.Command, MediaStreamKind.Data)]
  [TestCase(AsfTestMedia.Unknown, MediaStreamKind.Unknown)]
  [Category("Unit")]
  public void AStreamThatIsNeitherPicturesNorSoundIsStillAStream(AsfTestMedia media, MediaStreamKind expected) {
    // It has to be, or every stream after it would be renumbered — a stream's index is its position
    // among all of them, and leaving the ones nothing decodes out would move the rest.
    var container = AsfReader.FromBytes(AsfTestContainer.Build([new AsfTestStream { Media = media }]));

    Assert.That(AsfContainer.Streams(container)[0].Kind, Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void AStreamsNumberIsNotItsIndex() {
    // ASF numbers streams from one and may leave gaps; an index counts declarations from nought. A
    // reader that published the number would number this container's streams differently from every
    // other one here, and ffprobe reports these two as streams 0 and 1.
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream { Number = 3 }, new AsfTestStream { Number = 9, Media = AsfTestMedia.Audio }],
        [
          new AsfTestPacket {
            Payloads = [
              new AsfTestPayload { Stream = 9, Data = new byte[40] },
              new AsfTestPayload { Stream = 3, Data = new byte[60] },
            ],
          },
        ]));

    var streams = AsfContainer.Streams(container);
    var packets = AsfContainer.ReadPackets(container).ToList();

    Assert.Multiple(() => {
      Assert.That(streams.Select(s => s.Index), Is.EqualTo(new[] { 0, 1 }));
      Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(streams[1].Kind, Is.EqualTo(MediaStreamKind.Audio));

      // Stream 9 is index 1, and it is the payload that came first.
      Assert.That(packets.Select(p => p.StreamIndex), Is.EqualTo(new[] { 1, 0 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void APayloadForAStreamNothingDeclaredIsSkipped() {
    // Its index would be a position in a list with no such entry. ffprobe reports no such stream
    // either — a payload naming a stream the header never described is not a packet of anything.
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream { Number = 1 }],
        [
          new AsfTestPacket {
            Payloads = [
              new AsfTestPayload { Stream = 1, Data = new byte[50] },
              new AsfTestPayload { Stream = 5, Data = new byte[70] },
            ],
          },
        ]));

    var packets = AsfContainer.ReadPackets(container).ToList();

    Assert.That(packets.Select(p => p.Data.Length), Is.EqualTo(new[] { 50 }));
  }

  [Test]
  [Category("Unit")]
  public void AProtectedStreamStillHasAStreamNumber() {
    // The stream number is the low seven bits of a field whose top bit says the content is encrypted.
    // A reader that took the field whole would look for stream 32770 and find no packets at all, so
    // every protected file would demux to nothing.
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream { Number = 2, Encrypted = true }],
        [new AsfTestPacket { Payloads = [new AsfTestPayload { Stream = 2, Data = new byte[120] }] }]));

    Assert.Multiple(() => {
      Assert.That(AsfContainer.Streams(container), Has.Count.EqualTo(1));

      // The payloads are handed over as they lie. Whether anything can make sense of them is the
      // question a decoder answers, not the demuxer.
      Assert.That(AsfContainer.ReadPackets(container).Single().Data.Length, Is.EqualTo(120));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheWholeFormatDataGoesAcrossToTheCodec() {
    // A Windows Media Video 9 stream keeps its sequence header past the fortieth byte of the format
    // data, so a reader that handed over only the BITMAPINFOHEADER would have dropped the one thing
    // its decoder cannot start without.
    byte[] sequenceHeader = [0x4F, 0xDA, 0xCA, 0x00, 0x04, 0x00, 0x00, 0x00];
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build([new AsfTestStream { FourCharacterCode = "WMV3", ExtraFormatData = sequenceHeader }]));

    var stream = AsfContainer.Streams(container)[0];

    Assert.Multiple(() => {
      Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(40 + sequenceHeader.Length));
      Assert.That(stream.CodecPrivateData.Span[40..].ToArray(), Is.EqualTo(sequenceHeader));
    });
  }

  [Test]
  [Category("Unit")]
  public void AStreamDeclaredOnlyInsideAnExtendedStreamPropertiesObjectIsStillAStream() {
    // The format allows the whole Stream Properties Object to sit at the tail of an extended one, and
    // some files declare a stream nowhere else. One skipped here is a stream whose packets would come
    // out under a number nothing had described.
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream { Number = 2, DeclaredInsideExtendedProperties = true }],
        [new AsfTestPacket { Payloads = [new AsfTestPayload { Stream = 2, Data = new byte[80] }] }]));

    Assert.Multiple(() => {
      Assert.That(AsfContainer.Streams(container), Has.Count.EqualTo(1));
      Assert.That(AsfContainer.ReadPackets(container).Single().Data.Length, Is.EqualTo(80));
    });
  }

  [Test]
  [Category("Unit")]
  public void AFrameRateIsReportedOnlyWhereTheFileStatesOne() {
    // 400000 hundred-nanosecond units a frame is 25 frames a second, exactly.
    var stated = AsfReader.FromBytes(AsfTestContainer.Build([new AsfTestStream { AverageTimePerFrame = 400_000 }]));
    var silent = AsfReader.FromBytes(AsfTestContainer.Build([new AsfTestStream()]));

    Assert.Multiple(() => {
      Assert.That(AsfContainer.Streams(stated)[0].FrameRate.ToDouble(), Is.EqualTo(25d));

      // The obvious substitute — dividing the duration by the number of packets — is exactly the
      // interpolation this refuses. A file that states no rate has not stated one, and a number
      // invented here would be indistinguishable from a number in the file.
      Assert.That(AsfContainer.Streams(silent)[0].FrameRate.IsKnown, Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void AStreamsLanguageIsThePositionItNamesInTheLanguageList() {
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream { LanguageIndex = 1, Name = "Bildspur" }],
        languages: ["en-us", "de-de"]));

    var stream = AsfContainer.Streams(container)[0];

    Assert.Multiple(() => {
      Assert.That(stream.Language, Is.EqualTo("de-de"));
      Assert.That(stream.Name, Is.EqualTo("Bildspur"));
    });
  }

  [Test]
  [Category("Unit")]
  public void ALanguageIndexPastTheEndOfTheListIsNoLanguage() {
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build([new AsfTestStream { LanguageIndex = 4 }], languages: ["en-us"]));

    Assert.That(AsfContainer.Streams(container)[0].Language, Is.Null);
  }

  // ------------------------------------------------------------------------------------------
  // Packets
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void EveryWholeFrameComesOutOnceAtTheSizeItWasWritten() {
    var container = AsfReader.FromBytes(AsfTestContainer.Build("MP43", [_Frame(1, 900), _Frame(2, 700), _Frame(3, 800)]));

    var packets = AsfContainer.ReadPackets(container).ToList();

    Assert.Multiple(() => {
      Assert.That(packets.Select(p => p.Data.Length), Is.EqualTo(new[] { 900, 700, 800 }));
      Assert.That(packets[0].Data.ToArray(), Is.EqualTo(_Frame(1, 900)));
      Assert.That(packets[2].Data.ToArray(), Is.EqualTo(_Frame(3, 800)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ThePrerollComesOffEveryTimestamp() {
    // A file is written with its clock started early enough to fill a buffer before playback begins,
    // so every stated time is that far ahead. ffmpeg writes a preroll of 3100 milliseconds and a
    // reader that did not take it off would report every frame of every ffmpeg-written file that late.
    var container = AsfReader.FromBytes(AsfTestContainer.Build("MP43", [_Frame(1, 100), _Frame(2, 100), _Frame(3, 100)]));

    var packets = AsfContainer.ReadPackets(container).ToList();

    Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 0, 40, 80 }));
  }

  [Test]
  [Category("Unit")]
  public void ASinglePayloadPacketStatesNoLengthForItsPayload() {
    // The payload is whatever lies between the header and the padding, which only works because the
    // packet's own size is fixed. ffmpeg never writes this form; ffprobe reads it.
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream()],
        [
          new AsfTestPacket { SinglePayload = true, Payloads = [new AsfTestPayload { Data = _Frame(1, 500), KeyFrame = true }] },
          new AsfTestPacket { SinglePayload = true, Payloads = [new AsfTestPayload { Data = _Frame(2, 600), PresentationTime = 40 }] },
        ]));

    var packets = AsfContainer.ReadPackets(container).ToList();

    Assert.Multiple(() => {
      Assert.That(packets.Select(p => p.Data.Length), Is.EqualTo(new[] { 500, 600 }));
      Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 0, 40 }));
      Assert.That(packets[0].Data.ToArray(), Is.EqualTo(_Frame(1, 500)));
    });
  }

  [Test]
  [Category("Unit")]
  public void APacketWithNoErrorCorrectionBlockIsReadTheSame() {
    // The Error Correction Present bit is bit 7 of the first byte whether or not there is a block,
    // because when there is none that byte is already the Length Type Flags and the bit is in the same
    // place there. One test serves both, and a reader that always stepped over three bytes would
    // misread every packet of a file written without one.
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream()],
        [
          new AsfTestPacket { ErrorCorrection = false, Payloads = [new AsfTestPayload { Data = _Frame(1, 500), KeyFrame = true }] },
          new AsfTestPacket { ErrorCorrection = false, Payloads = [new AsfTestPayload { Data = _Frame(2, 600), PresentationTime = 40 }] },
        ]));

    var packets = AsfContainer.ReadPackets(container).ToList();

    Assert.Multiple(() => {
      Assert.That(packets.Select(p => p.Data.Length), Is.EqualTo(new[] { 500, 600 }));
      Assert.That(packets[1].Data.ToArray(), Is.EqualTo(_Frame(2, 600)));
    });
  }

  [Test]
  [Category("Unit")]
  public void APacketMayStateItsOwnLengthInsteadOfTakingTheFileSize() {
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream()],
        [
          new AsfTestPacket { ExplicitLength = true, Payloads = [new AsfTestPayload { Data = _Frame(1, 500), KeyFrame = true }] },
          new AsfTestPacket { ExplicitLength = true, Payloads = [new AsfTestPayload { Data = _Frame(2, 600), PresentationTime = 40 }] },
        ]));

    Assert.That(AsfContainer.ReadPackets(container).Select(p => p.Data.Length), Is.EqualTo(new[] { 500, 600 }));
  }

  [Test]
  [Category("Unit")]
  public void AFrameSplitAcrossPacketsComesOutWhole() {
    // The point of the type. ASF packets are a fixed size and frames are not, so a frame larger than a
    // packet is cut into pieces; a reader that handed the pieces out would report the shape of the
    // wire rather than the shape of the film, and its packet count would disagree with every tool's.
    var whole = _Frame(7, 5000);
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream()],
        [
          new AsfTestPacket { Payloads = [new AsfTestPayload { Data = whole[..2000], MediaObjectSize = 5000, KeyFrame = true }] },
          new AsfTestPacket { Payloads = [new AsfTestPayload { Data = whole[2000..4000], Offset = 2000, MediaObjectSize = 5000 }] },
          new AsfTestPacket { Payloads = [new AsfTestPayload { Data = whole[4000..], Offset = 4000, MediaObjectSize = 5000 }] },
          new AsfTestPacket { Payloads = [new AsfTestPayload { Data = _Frame(8, 400), PresentationTime = 40 }] },
        ]));

    var packets = AsfContainer.ReadPackets(container).ToList();

    Assert.Multiple(() => {
      Assert.That(packets.Select(p => p.Data.Length), Is.EqualTo(new[] { 5000, 400 }));
      Assert.That(packets[0].Data.ToArray(), Is.EqualTo(whole));

      // The whole object is due when its first piece said it was; the later pieces carry the same time
      // and say nothing new.
      Assert.That(packets[0].PresentationTimestamp, Is.EqualTo(0L));
      Assert.That(packets[0].IsKeyFrame, Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void APieceWhoseBeginningIsMissingIsNotHalfAFrame() {
    // What every stream looks like when reading begins in the middle of a file. There is no way to
    // make a frame of it, and half a frame handed on as a whole one is worse than nothing.
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream()],
        [
          new AsfTestPacket { Payloads = [new AsfTestPayload { Data = _Frame(1, 900), Offset = 2000, MediaObjectSize = 5000 }] },
          new AsfTestPacket { Payloads = [new AsfTestPayload { Data = _Frame(2, 400), PresentationTime = 40 }] },
        ]));

    Assert.That(AsfContainer.ReadPackets(container).Select(p => p.Data.Length), Is.EqualTo(new[] { 400 }));
  }

  [Test]
  [Category("Unit")]
  public void APieceThatDoesNotBeginWhereTheLastEndedDropsTheWholeFrame() {
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream()],
        [
          new AsfTestPacket { Payloads = [new AsfTestPayload { Data = _Frame(1, 1000), MediaObjectSize = 3000, KeyFrame = true }] },

          // A gap: this claims to start at 2000 where 1000 bytes have arrived. One piece went missing
          // and what is left cannot be completed, so it is dropped rather than handed on with a hole.
          new AsfTestPacket { Payloads = [new AsfTestPayload { Data = _Frame(2, 1000), Offset = 2000, MediaObjectSize = 3000 }] },
          new AsfTestPacket { Payloads = [new AsfTestPayload { Data = _Frame(3, 400), PresentationTime = 40 }] },
        ]));

    Assert.That(AsfContainer.ReadPackets(container).Select(p => p.Data.Length), Is.EqualTo(new[] { 400 }));
  }

  [Test]
  [Category("Unit")]
  public void ACompressedPayloadIsSeveralWholeFramesRatherThanAPieceOfOne() {
    // Replicated data of exactly one byte means the payload was built by the other rule: what would
    // have been the offset is the first frame's presentation time, the one replicated byte is the step
    // between frames, and the data is a run of frames each introduced by a byte of length. Read as a
    // fragment it would hand a decoder five frames glued into one.
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream { Media = AsfTestMedia.Audio }],
        [
          new AsfTestPacket {
            Payloads = [
              new AsfTestPayload {
                SubObjects = [_Frame(1, 100), _Frame(2, 100), _Frame(3, 100)],
                PresentationTimeDelta = 33,
                KeyFrame = true,
              },
            ],
          },
          new AsfTestPacket {
            Payloads = [
              new AsfTestPayload {
                SubObjects = [_Frame(4, 100), _Frame(5, 100)],
                PresentationTime = 99,
                PresentationTimeDelta = 33,
              },
            ],
          },
        ]));

    var packets = AsfContainer.ReadPackets(container).ToList();

    Assert.Multiple(() => {
      Assert.That(packets, Has.Count.EqualTo(5));
      Assert.That(packets.Select(p => p.Data.Length), Is.EqualTo(new[] { 100, 100, 100, 100, 100 }));

      // Each is the step further on than the one before it, which is the only place those times exist.
      Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 0, 33, 66, 99, 132 }));
      Assert.That(packets[1].Data.ToArray(), Is.EqualTo(_Frame(2, 100)));
    });
  }

  [Test]
  [Category("Unit")]
  public void SeveralWholeFramesMayShareOnePacket() {
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream { Number = 1 }, new AsfTestStream { Number = 3, Media = AsfTestMedia.Audio }],
        [
          new AsfTestPacket {
            Payloads = [
              new AsfTestPayload { Stream = 1, Data = _Frame(1, 300), KeyFrame = true },
              new AsfTestPayload { Stream = 3, Data = _Frame(2, 200) },
              new AsfTestPayload { Stream = 1, Data = _Frame(3, 300), PresentationTime = 40, MediaObjectNumber = 1 },
              new AsfTestPayload { Stream = 3, Data = _Frame(4, 200), PresentationTime = 46, MediaObjectNumber = 1 },
            ],
          },
        ]));

    var packets = AsfContainer.ReadPackets(container).ToList();

    Assert.Multiple(() => {
      Assert.That(packets.Select(p => p.StreamIndex), Is.EqualTo(new[] { 0, 1, 0, 1 }));
      Assert.That(packets.Select(p => p.Data.Length), Is.EqualTo(new[] { 300, 200, 300, 200 }));
      Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 0, 0, 40, 46 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheKeyFrameFlagIsTheOneTheFileStates() {
    // The top bit of the payload's stream-number byte, and nothing else. ffprobe reports every packet
    // of a sound stream as a key frame whatever that bit says, because an audio frame is independently
    // decodable — but that is a fact about the codec, and a demuxer that asserted it would be reporting
    // something no ASF file contains.
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream()],
        [
          new AsfTestPacket { Payloads = [new AsfTestPayload { Data = _Frame(1, 300), KeyFrame = true }] },
          new AsfTestPacket { Payloads = [new AsfTestPayload { Data = _Frame(2, 300), PresentationTime = 40 }] },
        ]));

    Assert.That(AsfContainer.ReadPackets(container).Select(p => p.IsKeyFrame), Is.EqualTo(new[] { true, false }));
  }

  [Test]
  [Category("Unit")]
  public void ADecodeTimestampIsNotInventedForAFormatThatStatesNone() {
    // ASF carries one time per media object and it is a presentation time. Copying it into the decode
    // timestamp would state, in a file that says nothing on the subject, that the frames are coded in
    // the order they are shown — which is false for anything with bidirectional prediction.
    var container = AsfReader.FromBytes(AsfTestContainer.Build("MP43", [_Frame(1, 300)]));

    Assert.That(AsfContainer.ReadPackets(container).Single().DecodeTimestamp, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void AWholeFrameIsAWindowOntoTheFileRatherThanACopy() {
    // A demuxer that copied the film in order to walk it would double it before a caller had asked for
    // one frame. Only a frame that genuinely arrived in pieces is copied, because its pieces are not
    // next to each other.
    var file = AsfTestContainer.Build("MP43", [_Frame(1, 900)]);
    var container = AsfReader.FromBytes(file);

    var packet = AsfContainer.ReadPackets(container).Single();
    var moved = System.Runtime.InteropServices.MemoryMarshal.TryGetArray(packet.Data, out var segment);

    Assert.Multiple(() => {
      Assert.That(moved, Is.True);
      Assert.That(segment.Array, Is.SameAs(file));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheWalkOfOneStreamIsTheFullWalkFiltered() {
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream { Number = 1 }, new AsfTestStream { Number = 2, Media = AsfTestMedia.Audio }],
        [
          new AsfTestPacket {
            Payloads = [
              new AsfTestPayload { Stream = 1, Data = _Frame(1, 300), KeyFrame = true },
              new AsfTestPayload { Stream = 2, Data = _Frame(2, 200) },
              new AsfTestPayload { Stream = 1, Data = _Frame(3, 250), PresentationTime = 40, MediaObjectNumber = 1 },
            ],
          },
        ]));

    var full = AsfContainer.ReadPackets(container).Where(p => p.StreamIndex == 0).Select(p => p.Data.Length).ToList();
    var filtered = AsfContainer.ReadPackets(container, 0).Select(p => p.Data.Length).ToList();

    Assert.That(filtered, Is.EqualTo(full));
  }

  [Test]
  [Category("Unit")]
  public void TheWalkMayBeRunMoreThanOnce() {
    // A film is not a list of packets held in memory. The walk is an enumeration over the file, and a
    // caller that ran it twice would otherwise get nothing the second time.
    var container = AsfReader.FromBytes(AsfTestContainer.Build("MP43", [_Frame(1, 300), _Frame(2, 400)]));

    var first = AsfContainer.ReadPackets(container).Select(p => p.Data.Length).ToList();
    var second = AsfContainer.ReadPackets(container).Select(p => p.Data.Length).ToList();

    Assert.That(second, Is.EqualTo(first));
  }

  [Test]
  [Category("Unit")]
  public void NothingIsReadUntilAPacketIsAskedFor() {
    // A two-hour recording enumerated for its first frame must cost one frame. The Data Object here is
    // cut off after the first packet: taking one packet has to work, and taking all of them must not
    // invent the rest.
    var file = AsfTestContainer.Build("MP43", [_Frame(1, 300), _Frame(2, 400), _Frame(3, 500)]);
    var container = AsfReader.FromBytes(file[..^(AsfTestContainer.PACKET_SIZE * 2)]);

    Assert.Multiple(() => {
      Assert.That(AsfContainer.ReadPackets(container).First().Data.Length, Is.EqualTo(300));
      Assert.That(AsfContainer.ReadPackets(container).Count(), Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void AFileCutShortKeepsWhatWasWritten() {
    // The declared count is the writer's claim; the bytes are the fact. A recording that stopped
    // mid-write keeps a count from before it stopped, and what was written is still perfectly readable.
    var file = AsfTestContainer.Build("MP43", [_Frame(1, 300), _Frame(2, 400)], step: 40);
    var container = AsfReader.FromBytes(file[..^600]);

    Assert.That(AsfContainer.ReadPackets(container).Select(p => p.Data.Length), Is.EqualTo(new[] { 300 }));
  }

  [Test]
  [Category("Unit")]
  public void ABroadcastIsWalkedToItsEndRatherThanToItsStatedCount() {
    // A broadcast was written without knowing how much there would be, so its count is nought and is
    // not a count. Believing it would read no packets out of a file full of them.
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream()],
        [
          new AsfTestPacket { Payloads = [new AsfTestPayload { Data = _Frame(1, 300), KeyFrame = true }] },
          new AsfTestPacket { Payloads = [new AsfTestPayload { Data = _Frame(2, 400), PresentationTime = 40 }] },
        ],
        broadcast: true,
        declaredPacketCount: 0));

    Assert.That(AsfContainer.ReadPackets(container).Select(p => p.Data.Length), Is.EqualTo(new[] { 300, 400 }));
  }

  [Test]
  [Category("Unit")]
  public void APaddingObjectInTheHeaderIsWalkedPast() {
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream()],
        [new AsfTestPacket { Payloads = [new AsfTestPayload { Data = _Frame(1, 300), KeyFrame = true }] }],
        padding: 512));

    Assert.Multiple(() => {
      Assert.That(AsfContainer.Streams(container), Has.Count.EqualTo(1));
      Assert.That(AsfContainer.ReadPackets(container).Single().Data.Length, Is.EqualTo(300));
    });
  }

  // ------------------------------------------------------------------------------------------
  // Malformed files
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void AFileSmashedAnywhereIsRefusedOrReadButNeverCrashes() {
    // A demuxer reads files it did not write. The only acceptable outcomes for a broken one are a
    // refusal or a short, finite walk — never an exception from an index running off the end of the
    // array, and never a walk that does not stop.
    //
    // Three defects of exactly that kind were found here by doing this over a hundred thousand
    // mutations, and every one of them was a length field the file states in four bytes: a type-specific
    // data length, a padding length and a replicated data length. Each is unsigned in the format and
    // each, narrowed to a signed integer before it was range-checked, became negative and passed a
    // check that was meant to stop it — after which the cursor walked backwards out of the file.
    var seed = AsfTestContainer.Build(
      [new AsfTestStream { Number = 1 }, new AsfTestStream { Number = 2, Media = AsfTestMedia.Audio }],
      [
        new AsfTestPacket {
          Payloads = [
            new AsfTestPayload { Stream = 1, Data = _Frame(1, 400), MediaObjectSize = 900, KeyFrame = true },
            new AsfTestPayload { Stream = 2, Data = _Frame(2, 200) },
          ],
        },
        new AsfTestPacket {
          Payloads = [new AsfTestPayload { Stream = 1, Data = _Frame(3, 500), Offset = 400, MediaObjectSize = 900 }],
        },
      ],
      title: "Ein Titel",
      descriptors: [("WM/Picture", 1, AsfTestContainer.Picture(3, "image/png", "Titelbild", _Frame(9, 32)))],
      languages: ["de-de"],
      codecs: [(1, "Windows Media Video 9", "")]);

    var random = new Random(20260821);
    var failures = new List<string>();

    for (var round = 0; round < 4000; ++round) {
      var copy = (byte[])seed.Clone();

      // The header region, where the length fields are, gets hit far more often than the payload bytes,
      // which are only ever copied.
      for (var hit = random.Next(1, 6); hit > 0; --hit)
        copy[random.Next(Math.Min(copy.Length, 1400))] = (byte)random.Next(256);

      try {
        var container = AsfReader.FromBytes(copy);
        var seen = 0;
        foreach (var packet in AsfContainer.ReadPackets(container))
          if (++seen > 10_000) {
            failures.Add($"round {round}: the walk did not stop");
            break;
          }

        _ = AsfContainer.Metadata(container);
      } catch (InvalidDataException) {
        // A refusal is the other right answer.
      } catch (Exception e) {
        failures.Add($"round {round}: {e.GetType().Name}");
      }
    }

    Assert.That(failures, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AFrameLongerThanTheFileIsNotAllocated() {
    // The stated size of a media object is four bytes of the file like any other, and a frame cannot be
    // longer than the packets it has to arrive in. Believing one that claims two gigabytes would ask
    // for two gigabytes to hold pieces that do not exist — a malformed file should cost a refused
    // frame, not the memory of the machine reading it.
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream()],
        [
          new AsfTestPacket { Payloads = [new AsfTestPayload { Data = _Frame(1, 400), MediaObjectSize = int.MaxValue, KeyFrame = true }] },
          new AsfTestPacket { Payloads = [new AsfTestPayload { Data = _Frame(2, 300), PresentationTime = 40 }] },
        ]));

    Assert.That(AsfContainer.ReadPackets(container).Select(p => p.Data.Length), Is.EqualTo(new[] { 300 }));
  }

  [Test]
  [Category("Unit")]
  public void AVideoStreamStatingTooLittleFormatDataIsRefusedByName() {
    var file = AsfTestContainer.Build([new AsfTestStream()]);

    // The Format Data Size field of the video stream's type-specific data, cut to eight bytes — fewer
    // than a bitmap header occupies, however much room the object itself leaves.
    var at = _IndexOf(file, [0x28, 0x00, 0x00, 0x00]);
    file[at - 2] = 0x08;
    file[at - 1] = 0x00;

    var refusal = Assert.Throws<InvalidDataException>(() => AsfReader.FromBytes(file));

    Assert.That(refusal!.Message, Does.Contain("format data"));
  }

  // ------------------------------------------------------------------------------------------
  // Metadata
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void TheContentDescriptionIsRead() {
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream()],
        title: "Ein Titel",
        author: "Der Autor",
        copyright: "(c) 2026",
        description: "Eine Beschreibung",
        rating: "5"));

    var metadata = AsfContainer.Metadata(container);

    Assert.Multiple(() => {
      Assert.That(metadata.Title, Is.EqualTo("Ein Titel"));
      Assert.That(metadata.Artist, Is.EqualTo("Der Autor"));

      // The model has no field for the last three, and a reader that dropped what it had no field for
      // would be indistinguishable from a file that never carried them.
      Assert.That(_Text(metadata, "Copyright"), Is.EqualTo("(c) 2026"));
      Assert.That(_Text(metadata, "Description"), Is.EqualTo("Eine Beschreibung"));
      Assert.That(_Text(metadata, "Rating"), Is.EqualTo("5"));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheExtendedContentDescriptionIsRead() {
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream()],
        descriptors: [
          ("WM/AlbumTitle", 0, Encoding.Unicode.GetBytes("Das Album\0")),
          ("WM/TrackNumber", 3, [7, 0, 0, 0]),
          ("WM/EncodingSettings", 0, Encoding.Unicode.GetBytes("Lavf63.1.100\0")),
          ("IsVBR", 2, [1, 0, 0, 0]),
        ]));

    var metadata = AsfContainer.Metadata(container);

    Assert.Multiple(() => {
      Assert.That(metadata.Album, Is.EqualTo("Das Album"));
      Assert.That(metadata.EncodedBy, Is.EqualTo("Lavf63.1.100"));

      // The format spells a number four ways and none of them is text. A number rendered as text is
      // what an annotation is for; dropping it would lose what the file said.
      Assert.That(_Text(metadata, "WM/TrackNumber"), Is.EqualTo("7"));
      Assert.That(_Text(metadata, "IsVBR"), Is.EqualTo("True"));
    });
  }

  [Test]
  [Category("Unit")]
  public void ATitleWrittenTwiceIsNotReportedTwice() {
    // ffmpeg writes the title into the Content Description Object and again, in lower case, into the
    // Extended Content Description. Matching one spelling only would file the other as an annotation
    // beside the value it duplicates, and the file would appear to carry its title twice.
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream()],
        title: "Ein Titel",
        descriptors: [("title", 0, Encoding.Unicode.GetBytes("Ein Titel\0"))]));

    var metadata = AsfContainer.Metadata(container);

    Assert.Multiple(() => {
      Assert.That(metadata.Title, Is.EqualTo("Ein Titel"));
      Assert.That(metadata.TextEntries.Select(e => e.Keyword), Does.Not.Contain("title"));
    });
  }

  [Test]
  [Category("Unit")]
  public void ACoverPictureIsKeptInTheFormatItWasEmbeddedAs() {
    var picture = _Frame(9, 64);
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream()],
        descriptors: [("WM/Picture", 1, AsfTestContainer.Picture(3, "image/png", "Titelbild", picture))]));

    var covers = AsfContainer.Metadata(container).CoverArt;

    Assert.Multiple(() => {
      Assert.That(covers, Has.Count.EqualTo(1));

      // Not decoded. That is what a muxer writing another container has to hand over, and decoding it
      // first could only lose the original.
      Assert.That(covers[0].Data, Is.EqualTo(picture));
      Assert.That(covers[0].MimeType, Is.EqualTo("image/png"));
      Assert.That(covers[0].Description, Is.EqualTo("Titelbild"));
      Assert.That(covers[0].Kind, Is.EqualTo("cover"));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheCodecListSaysInWordsWhatIsInside() {
    // The only place a file says in words what it holds, which is worth having when the four-character
    // code names a codec nothing here reads. Its two string lengths count characters where every other
    // length in the format counts bytes.
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build(
        [new AsfTestStream()],
        codecs: [(1, "Windows Media Video 9", "Sehr gut"), (2, "Windows Media Audio V8", "")]));

    var metadata = AsfContainer.Metadata(container);

    Assert.Multiple(() => {
      Assert.That(_Text(metadata, "Video Codec"), Is.EqualTo("Windows Media Video 9 (Sehr gut)"));
      Assert.That(_Text(metadata, "Audio Codec"), Is.EqualTo("Windows Media Audio V8"));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheDeclaredDurationHasThePrerollTakenOff() {
    // The play duration counts the preroll as well as the film, because it is how long the clock runs
    // and the clock starts early. ffprobe reports the difference, not the field.
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build([new AsfTestStream()], playDuration: 2 * AsfTestContainer.UNITS_PER_SECOND));

    Assert.That(AsfContainer.Metadata(container).Duration, Is.EqualTo(TimeSpan.FromSeconds(2)));
  }

  [Test]
  [Category("Unit")]
  public void ABroadcastStatesNoDuration() {
    var container = AsfReader.FromBytes(AsfTestContainer.Build([new AsfTestStream()], broadcast: true));

    Assert.That(AsfContainer.Metadata(container).Duration, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void ACreationDateIsCountedFrom1601() {
    // The field is a Windows file time, which is neither the Unix epoch nor what any other container
    // here counts from.
    var when = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build([new AsfTestStream()], creationDate: (ulong)when.ToFileTime()));

    Assert.That(AsfContainer.Metadata(container).CreationTime, Is.EqualTo(when));
  }

  [Test]
  [Category("Unit")]
  public void AFileThatStatesNoCreationDateIsNotDatedTo1601() {
    var container = AsfReader.FromBytes(AsfTestContainer.Build([new AsfTestStream()]));

    Assert.That(AsfContainer.Metadata(container).CreationTime, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void EveryStreamIsListedInTheMetadata() {
    var container = AsfReader.FromBytes(
      AsfTestContainer.Build([new AsfTestStream { Number = 1 }, new AsfTestStream { Number = 2, Media = AsfTestMedia.Audio }]));

    var streams = AsfContainer.Metadata(container).Streams;

    Assert.Multiple(() => {
      Assert.That(streams, Has.Count.EqualTo(2));
      Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(streams[1].Kind, Is.EqualTo(MediaStreamKind.Audio));
    });
  }

  // ------------------------------------------------------------------------------------------
  // Identity
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void TheHeaderObjectsIdentifierIsTheSignature() {
    var file = _Simple();

    Assert.Multiple(() => {
      Assert.That(AsfContainer.MatchesSignature(file), Is.True);

      // No opinion rather than a refusal: the header of some other format is not this container's to
      // rule on, and the registry has other candidates to try.
      Assert.That(AsfContainer.MatchesSignature("RIFF....AVI "u8), Is.Null);
      Assert.That(AsfContainer.MatchesSignature([0x30, 0x26]), Is.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void OneFormatUnderThreeExtensions()
    => Assert.Multiple(() => {
      Assert.That(AsfContainer.PrimaryExtension, Is.EqualTo(".asf"));

      // Nothing in the file distinguishes them: a .wmv is an ASF whose first stream carries pictures
      // and a .wma one whose streams carry only sound.
      Assert.That(AsfContainer.FileExtensions, Does.Contain(".wmv"));
      Assert.That(AsfContainer.FileExtensions, Does.Contain(".wma"));
    });

  [Test]
  [Category("Unit")]
  public void TheContainerIsDiscoveredAndRegistered() {
    // Nothing was edited to say so: the source generator finds the type by the interface it declares,
    // which is what lets a container be added in one file and a codec in another without either
    // mentioning the other.
    var file = _Simple();

    Assert.Multiple(() => {
      Assert.That(Hawkynt.FileFormats.Video.VideoFormatRegistry.Detect(file), Is.EqualTo(Hawkynt.FileFormats.Video.VideoFormat.Asf));
      Assert.That(Hawkynt.FileFormats.Video.VideoFormatRegistry.ByExtension(".wmv"), Does.Contain(Hawkynt.FileFormats.Video.VideoFormat.Asf));
      Assert.That(Hawkynt.FileFormats.Video.VideoFormatRegistry.ReadPackets(file).Count(), Is.EqualTo(2));
    });
  }

  [Test]
  [Category("Unit")]
  public void AStreamNothingDecodesStillDemuxes() {
    // The whole point of the split: a file whose codec nothing here reads is still a perfectly good
    // ASF whose packets can be counted, inspected and copied into another container.
    //
    // The code is a made-up one rather than a real codec that happens to have no decoder yet. A real
    // one would make this test a statement about which codecs are implemented, and it would start
    // failing the day somebody implemented that one — which is the opposite of what it is for.
    var file = AsfTestContainer.Build("ZZZZ", [_Frame(1, 300), _Frame(2, 400)]);
    var streams = Hawkynt.FileFormats.Video.VideoFormatRegistry.ReadStreams(file);

    Assert.Multiple(() => {
      Assert.That(streams[0].Codec, Is.EqualTo(CodecTag.FromCharacters("ZZZZ")));
      Assert.That(Hawkynt.FileFormats.Video.VideoFormatRegistry.CanDecode(streams[0]), Is.False);
      Assert.That(Hawkynt.FileFormats.Video.VideoFormatRegistry.ReadPackets(file).Select(p => p.Data.Length), Is.EqualTo(new[] { 300, 400 }));

      // And the refusal names the codec, because nobody recognises the number.
      var refusal = Assert.Throws<NotSupportedException>(
        () => Hawkynt.FileFormats.Video.VideoFormatRegistry.CreateDecoder(streams[0]));
      Assert.That(refusal!.Message, Does.Contain("ZZZZ"));
    });
  }

  // ------------------------------------------------------------------------------------------
  // Helpers
  // ------------------------------------------------------------------------------------------

  /// <summary>Bytes that are not a picture and are not meant to be: a demuxer never looks inside one.</summary>
  private static byte[] _Frame(int seed, int length) {
    var data = new byte[length];
    for (var i = 0; i < length; ++i)
      data[i] = (byte)((seed * 31) + i);

    return data;
  }

  private static byte[] _Simple() => AsfTestContainer.Build("MP43", [_Frame(1, 300), _Frame(2, 400)]);

  /// <summary>Where a run of bytes first appears, so a test can reach a field without counting offsets.</summary>
  private static int _IndexOf(byte[] haystack, byte[] needle) {
    for (var at = 0; at + needle.Length <= haystack.Length; ++at) {
      var found = true;
      for (var i = 0; i < needle.Length; ++i)
        if (haystack[at + i] != needle[i]) {
          found = false;
          break;
        }

      if (found)
        return at;
    }

    throw new InvalidOperationException("The bytes the test meant to reach are not in the file it built.");
  }

  private static string? _Text(VideoMetadata metadata, string keyword) {
    foreach (var entry in metadata.TextEntries)
      if (entry.Keyword == keyword)
        return entry.Text;

    return null;
  }
}
