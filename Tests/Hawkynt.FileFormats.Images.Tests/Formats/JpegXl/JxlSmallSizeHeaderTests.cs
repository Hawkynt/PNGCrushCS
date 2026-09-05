using System;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// How a picture states its width when its height was stated in eighths.
/// </summary>
/// <remarks>
/// A picture whose height divides by eight and is no more than 2,048 states
/// that height as five bits of eighths, and then three bits naming its shape.
/// Where the shape is one of the seven the format lists the width follows from
/// it; where it is not, the width is stated — and it is stated the same way the
/// height was, five bits of eighths, not in the four-selector form a larger
/// picture uses. Reading the wrong one costs six bits and takes the whole rest
/// of the codestream with it, which is what it did: a 200x256 file failed on
/// its very first field.
/// </remarks>
[TestFixture]
internal sealed class JxlSmallSizeHeaderTests {

  /// <summary>The first two bytes of a 200x256 file cjxl 0.12.0 wrote. Its
  /// height divides by eight, and 200 by 256 is none of the seven shapes, so
  /// the width is stated outright.</summary>
  [Test]
  public void ASmallPictureOfNoNamedShapeStatesItsWidthInEighths() {
    var reader = new JxlBitReader([0x3F, 0xF0], 0);

    var (width, height) = JxlSizeHeader.Decode(reader);

    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(200));
      Assert.That(height, Is.EqualTo(256));
      // small, five of height, three of shape, five of width.
      Assert.That(reader.BitsRead, Is.EqualTo(1 + 5 + 3 + 5));
    });
  }

  /// <param name="ratio">The three-bit shape, as the format numbers them.</param>
  [TestCase(1, 256, TestName = "square")]
  [TestCase(3, 341, TestName = "four by three")]
  [TestCase(7, 512, TestName = "two by one")]
  public void ASmallPictureOfANamedShapeTakesItsWidthFromTheShape(int ratio, int expectedWidth) {
    // small = 1, height_div8 - 1 = 31 (so 256), then the shape.
    var bits = 1u | (31u << 1) | ((uint)ratio << 6);
    var reader = new JxlBitReader([(byte)(bits & 0xFF), (byte)(bits >> 8)], 0);

    var (width, height) = JxlSizeHeader.Decode(reader);

    Assert.Multiple(() => {
      Assert.That(height, Is.EqualTo(256));
      Assert.That(width, Is.EqualTo(expectedWidth));
      Assert.That(reader.BitsRead, Is.EqualTo(1 + 5 + 3), "a named shape states no width of its own");
    });
  }

  /// <summary>The first two bytes of a 200x150 file cjxl wrote. Its height does
  /// not divide by eight, so nothing here applies to it and it is read the
  /// other way.</summary>
  [Test]
  public void APictureWhoseHeightDoesNotDivideByEightIsUnaffected() {
    var reader = new JxlBitReader([0xA8, 0xB4], 0);

    var (width, height) = JxlSizeHeader.Decode(reader);

    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(200));
      Assert.That(height, Is.EqualTo(150));
      // not small, two of selector, nine of height, three of shape.
      Assert.That(reader.BitsRead, Is.EqualTo(1 + 2 + 9 + 3));
    });
  }
}
