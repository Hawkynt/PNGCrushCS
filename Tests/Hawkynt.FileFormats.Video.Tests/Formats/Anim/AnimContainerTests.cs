using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Anim.Tests;

/// <summary>
/// IFF ANIM's demuxing behaviour: finding where one <c>FORM ILBM</c> frame ends and the next begins,
/// without decoding any of what is inside one. Frame-level delta decoding is not exercised here — <see
/// cref="Codecs.Tests.AnimVideoDecoderTests"/> covers that, and four real files were compared frame by
/// frame against ffmpeg's decode with no differing sample.
/// </summary>
[TestFixture]
public sealed class AnimContainerTests {

  // ============================================================================================
  // Opening
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFileTooShortForTheHeaderIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(() => AnimContainer.FromBytes(new byte[8]));
    Assert.That(failure!.Message, Does.Contain("ANIM"));
  }

  [Test]
  [Category("Unit")]
  public void AFileNotOpeningWithFormAnimIsRefused() {
    var notAnim = new byte[16];
    "FORM"u8.CopyTo(notAnim);
    "ILBM"u8.CopyTo(notAnim.AsSpan(8));

    var failure = Assert.Throws<NotSupportedException>(() => AnimContainer.FromBytes(notAnim));
    Assert.That(failure!.Message, Does.Contain("ANIM"));
  }

  [Test]
  [Category("Unit")]
  public void AFileWithNoIlbmFrameOpensWithZeroFrames() {
    var file = _AnimFile(); // no frames appended
    var container = AnimContainer.FromBytes(file);

    Assert.That(container.FrameCount, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void APlausibleFileOpensAndReadsWidthAndHeightFromTheFirstFramesBmhd() {
    var file = _AnimFile(_Keyframe(width: 16, height: 8, planes: 1));
    var container = AnimContainer.FromBytes(file);

    Assert.That(container.Width, Is.EqualTo(16));
    Assert.That(container.Height, Is.EqualTo(8));
    Assert.That(container.FrameCount, Is.EqualTo(1));
  }

  // ============================================================================================
  // Signature
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void MatchesSignatureAcceptsAGenuineFormAnimHeader() {
    var file = _AnimFile(_Keyframe(width: 4, height: 4, planes: 1));

    Assert.That(AnimContainer.MatchesSignature(file), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void MatchesSignatureDoesNotAcceptAPlainFormIlbm() {
    var header = new byte[12];
    "FORM"u8.CopyTo(header);
    "ILBM"u8.CopyTo(header.AsSpan(8));

    Assert.That(AnimContainer.MatchesSignature(header), Is.Not.True);
  }

  // ============================================================================================
  // Streams and packets
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DeclaresExactlyOneVideoStream() {
    var file = _AnimFile(_Keyframe(width: 4, height: 4, planes: 1));
    var container = AnimContainer.FromBytes(file);

    var streams = AnimContainer.Streams(container);
    Assert.That(streams, Has.Count.EqualTo(1));
    Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
    Assert.That(streams[0].Codec.ToString(), Is.EqualTo("ANIM"));
  }

  [Test]
  [Category("Unit")]
  public void EveryPacketGoesOnStreamZeroInFileOrder() {
    var file = _AnimFile(
      _Keyframe(width: 4, height: 4, planes: 1),
      _DeltaFrame([]));
    var container = AnimContainer.FromBytes(file);

    var packets = AnimContainer.ReadPackets(container).ToArray();
    Assert.That(packets, Has.Length.EqualTo(2));
    Assert.That(packets.All(p => p.StreamIndex == 0), Is.True);
    Assert.That(packets[0].PresentationTimestamp, Is.EqualTo(0));
    Assert.That(packets[1].PresentationTimestamp, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void OnlyTheFirstPacketIsAKeyFrame() {
    var file = _AnimFile(
      _Keyframe(width: 4, height: 4, planes: 1),
      _DeltaFrame([]));
    var container = AnimContainer.FromBytes(file);

    var packets = AnimContainer.ReadPackets(container).ToArray();
    Assert.That(packets[0].IsKeyFrame, Is.True);
    Assert.That(packets[1].IsKeyFrame, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void APacketCarriesTheWholeFormIlbmIncludingItsSubChunks() {
    var frame = _Keyframe(width: 4, height: 4, planes: 1);
    var file = _AnimFile(frame);
    var container = AnimContainer.FromBytes(file);

    var packet = AnimContainer.ReadPackets(container).Single();
    Assert.That(packet.Data.Length, Is.EqualTo(frame.Length));
    Assert.That(packet.Data.Span[8..12].ToArray(), Is.EqualTo("ILBM"u8.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void AFrameThatDoesNotFullyFitInWhatRemainsStopsTheWalkCleanly() {
    var frame = _Keyframe(width: 4, height: 4, planes: 1);
    var file = _AnimFile(frame);
    var truncated = file[..^4];

    Assert.DoesNotThrow(() => AnimContainer.FromBytes(truncated));
  }

  [Test]
  [Category("Unit")]
  public void ANonIlbmTopLevelChunkIsSteppedOverRatherThanBreakingTheWalk() {
    var junk = _Chunk("JUNK", [1, 2, 3, 4]);
    var frame = _Keyframe(width: 4, height: 4, planes: 1);
    var body = new byte[junk.Length + frame.Length];
    junk.CopyTo(body, 0);
    frame.CopyTo(body, junk.Length);
    var file = _WrapAnim(body);

    var container = AnimContainer.FromBytes(file);
    Assert.That(container.FrameCount, Is.EqualTo(1));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static byte[] _Chunk(string id, byte[] data) {
    var chunk = new byte[8 + data.Length + (data.Length & 1)];
    System.Text.Encoding.ASCII.GetBytes(id).CopyTo(chunk, 0);
    BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(4), (uint)data.Length);
    data.CopyTo(chunk, 8);
    return chunk;
  }

  private static byte[] _Form(string formType, params byte[][] chunks) {
    var innerLength = 4 + chunks.Sum(c => c.Length);
    var form = new byte[8 + innerLength];
    "FORM"u8.CopyTo(form);
    BinaryPrimitives.WriteUInt32BigEndian(form.AsSpan(4), (uint)innerLength);
    System.Text.Encoding.ASCII.GetBytes(formType).CopyTo(form, 8);
    var at = 12;
    foreach (var chunk in chunks) {
      chunk.CopyTo(form, at);
      at += chunk.Length;
    }

    return form;
  }

  internal static byte[] _Keyframe(int width, int height, int planes, byte compression = 0, byte[]? bodyBytes = null) {
    var bmhd = new byte[20];
    BinaryPrimitives.WriteUInt16BigEndian(bmhd.AsSpan(0), (ushort)width);
    BinaryPrimitives.WriteUInt16BigEndian(bmhd.AsSpan(2), (ushort)height);
    bmhd[8] = (byte)planes;
    bmhd[10] = compression;

    var bytesPerRow = (width + 15) / 16 * 2;
    var body = bodyBytes ?? new byte[bytesPerRow * height * planes];

    return _Form("ILBM", _Chunk("BMHD", bmhd), _Chunk("BODY", body));
  }

  internal static byte[] _DeltaFrame(byte[] dltaData, byte operation = 5, byte interleave = 0, uint bits = 0) {
    var anhd = new byte[40];
    anhd[0] = operation;
    anhd[18] = interleave;
    BinaryPrimitives.WriteUInt32BigEndian(anhd.AsSpan(20), bits);

    return _Form("ILBM", _Chunk("ANHD", anhd), _Chunk("DLTA", dltaData));
  }

  private static byte[] _WrapAnim(byte[] body) {
    var file = new byte[8 + 4 + body.Length];
    "FORM"u8.CopyTo(file);
    BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4), (uint)(4 + body.Length));
    "ANIM"u8.CopyTo(file.AsSpan(8));
    body.CopyTo(file, 12);
    return file;
  }

  private static byte[] _AnimFile(params byte[][] frames) => _WrapAnim(frames.SelectMany(f => f).ToArray());
}
