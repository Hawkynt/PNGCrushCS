using System;
using System.Collections.Generic;
using FileFormat.Core;
using NUnit.Framework;

namespace FileFormat.Core.Tests;

[TestFixture]
public sealed class DecodedTemporalFrameGroupTests {
  [Test]
  [Category("Unit")]
  public void PreservesPresentationOrderTimingAndStreamIdentity() {
    var first = _Frame(10, 64, 48, PixelFormat.Rgb24, 100, isKeyFrame: true);
    var second = _Frame(10, 64, 48, PixelFormat.Rgb24, 101);
    var group = new DecodedTemporalFrameGroup([first, second]);

    Assert.Multiple(() => {
      Assert.That(group.Count, Is.EqualTo(2));
      Assert.That(group.StreamIndex, Is.EqualTo(10));
      Assert.That(group.Width, Is.EqualTo(64));
      Assert.That(group.Height, Is.EqualTo(48));
      Assert.That(group.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(group[0].PresentationTimestamp, Is.EqualTo(100));
      Assert.That(group[1].PresentationTimestamp, Is.EqualTo(101));
      Assert.That(group[0].IsKeyFrame, Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void RequiresAtLeastTwoFrames() {
    Assert.Throws<ArgumentException>(() => new DecodedTemporalFrameGroup([_Frame(0, 16, 16, PixelFormat.Gray8, 0)]));
  }

  [Test]
  [Category("Unit")]
  public void RejectsNullRastersWithoutDereferencingThem() {
    var valid = _Frame(1, 16, 16, PixelFormat.Gray8, 1);
    var missing = new DecodedFrame(null!, 1, 0);

    Assert.Multiple(() => {
      Assert.Throws<ArgumentException>(() => new DecodedTemporalFrameGroup([missing, valid]));
      Assert.Throws<ArgumentException>(() => new DecodedTemporalFrameGroup([valid, missing]));
    });
  }

  [Test]
  [Category("Unit")]
  public void RequiresOneStreamAndOneRasterRepresentationWithinTheTransformGroup() {
    var first = _Frame(1, 64, 48, PixelFormat.Rgb24, 10);

    Assert.Multiple(() => {
      Assert.Throws<ArgumentException>(() => new DecodedTemporalFrameGroup([
        first,
        _Frame(2, 64, 48, PixelFormat.Rgb24, 11),
      ]));
      Assert.Throws<ArgumentException>(() => new DecodedTemporalFrameGroup([
        first,
        _Frame(1, 80, 48, PixelFormat.Rgb24, 11),
      ]));
      Assert.Throws<ArgumentException>(() => new DecodedTemporalFrameGroup([
        first,
        _Frame(1, 64, 48, PixelFormat.Rgba32, 11),
      ]));
    });
  }

  [Test]
  [Category("Unit")]
  public void KnownAdjacentPresentationTimesMustIncrease() {
    var first = _Frame(1, 64, 48, PixelFormat.Rgb24, 50);

    Assert.Multiple(() => {
      Assert.Throws<ArgumentException>(() => new DecodedTemporalFrameGroup([
        first,
        _Frame(1, 64, 48, PixelFormat.Rgb24, 50),
      ]));
      Assert.Throws<ArgumentException>(() => new DecodedTemporalFrameGroup([
        first,
        _Frame(1, 64, 48, PixelFormat.Rgb24, 49),
      ]));
    });
  }

  [Test]
  [Category("Unit")]
  public void UnknownPresentationTimesDoNotInventTiming() {
    var group = new DecodedTemporalFrameGroup([
      _Frame(1, 64, 48, PixelFormat.Rgb24, null),
      _Frame(1, 64, 48, PixelFormat.Rgb24, null),
    ]);

    Assert.That(group.Frames, Has.All.Property(nameof(DecodedFrame.PresentationTimestamp)).Null);
  }

  [Test]
  [Category("Unit")]
  public void ExposedFrameCollectionIsReadOnly() {
    var group = new DecodedTemporalFrameGroup([
      _Frame(1, 64, 48, PixelFormat.Rgb24, 0),
      _Frame(1, 64, 48, PixelFormat.Rgb24, 1),
    ]);

    var collection = (ICollection<DecodedFrame>)group.Frames;
    Assert.That(collection.IsReadOnly, Is.True);
    Assert.Throws<NotSupportedException>(() => collection.Add(_Frame(1, 64, 48, PixelFormat.Rgb24, 2)));
  }

  private static DecodedFrame _Frame(
    int streamIndex,
    int width,
    int height,
    PixelFormat format,
    long? presentationTimestamp,
    bool isKeyFrame = false) {

    var bytesPerPixel = format switch {
      PixelFormat.Gray8 => 1,
      PixelFormat.Rgb24 => 3,
      PixelFormat.Rgba32 => 4,
      _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    return new(
      new RawImage {
        Width = width,
        Height = height,
        Format = format,
        PixelData = new byte[width * height * bytesPerPixel],
      },
      streamIndex,
      presentationTimestamp,
      isKeyFrame);
  }
}
