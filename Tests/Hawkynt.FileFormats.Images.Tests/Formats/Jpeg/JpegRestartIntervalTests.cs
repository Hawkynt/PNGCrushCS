using System;
using System.Linq;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Jpeg.Tests;

/// <summary>A JPEG whose entropy data is broken into intervals by restart markers.</summary>
/// <remarks>
/// Reaching a marker is what ends the run of bits before it, so the reader is sitting at the end of
/// its data by the time the marker is stepped over; stepping over it has to lift that. It did not, so
/// every interval after the first read zeros and the picture came out one flat mid-grey whatever was
/// in it. Cameras write restart intervals as a matter of course, and a file written with one MCU per
/// interval — as here — lost all but its first eight rows of eight pixels.
/// </remarks>
[TestFixture]
public sealed class JpegRestartIntervalTests {

  /// <summary>A 64x64 black-to-white vertical gradient, one MCU per restart interval (63 markers).</summary>
  private const string _Gradient =
      "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcp"
    + "LDAxNDQ0Hyc5PTgyPC4zNDL/wAALCABAAEABAREA/8QAFgABAQEAAAAAAAAAAAAAAAAABwUG/8QAFhAAAwAAAAAAAAAAAAAA"
    + "AAAAABRh/90ABAAB/9oACAEBAAA/AA1OH//QDU4f/9ENTh//0g1OH//TDU4f/9QNTh//1Q1OH//WDU4f/9cuSh//0C5KH//R"
    + "Lkof/9IuSh//0y5KH//ULkof/9UuSh//1i5KH//XxycP/9DHJw//0ccnD//SxycP/9PHJw//1McnD//VxycP/9bHJw//15yc"
    + "P//QnJw//9GcnD//0pycP//TnJw//9ScnD//1ZycP//WnJw//9egnD//0KCcP//RoJw//9KgnD//06CcP//UoJw//9WgnD//"
    + "1qCcP//X2CcP/9DYJw//0dgnD//S2CcP/9PYJw//1NgnD//V2CcP/9bYJw//11BKH//QUEof/9FQSh//0lBKH//TUEof/9RQ"
    + "Sh//1VBKH//WUEof/9dxTh//0HFOH//RcU4f/9JxTh//03FOH//UcU4f/9VxTh//1nFOH//Z"
    ;

  [Test]
  [Category("Unit")]
  public void Every_interval_after_the_first_still_carries_its_picture() {
    var file = JpegReader.FromSpan(Convert.FromBase64String(_Gradient));
    var rgb = JpegFile.ToRawImage(file).ToRgb24();

    byte Grey(int y) => rgb[((y * file.Width) + 32) * 3];

    Assert.Multiple(() => {
      Assert.That((file.Width, file.Height), Is.EqualTo((64, 64)));
      Assert.That(Grey(2), Is.LessThan((byte)40), "it starts black");
      Assert.That(Grey(61), Is.GreaterThan((byte)200), "and ends white");

      // The failure was not a wrong shade but a missing one: past the first interval every row read
      // the same. A gradient that still climbs is a gradient whose intervals were all decoded.
      var rows = Enumerable.Range(0, 64).Select(Grey).ToArray();
      Assert.That(rows.Distinct().Count(), Is.GreaterThan(16), "the ramp survives, rather than flattening after the first interval");
      Assert.That(rows[48], Is.GreaterThan(rows[16]), "and it climbs in the right direction");
    });
  }
}
