using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Codecs.Ffv1.Tests;

/// <summary>Known-answer interoperability vectors generated and decoded with FFmpeg 7.1.5.</summary>
/// <remarks>
/// The packet bytes and version 3 configuration records come from FFmpeg's FFV1 encoder. The expected
/// raw bytes are FFmpeg's own decode of those files. Keeping both sides as fixed data makes these
/// interoperability tests executable-independent: CI does not need an FFmpeg binary.
/// </remarks>
[TestFixture]
public class Ffv1ReferenceVectorTests {

  [Test]
  [Category("Unit")]
  [TestCaseSource(nameof(_Vectors))]
  public void FFmpegEncodedPacketsDecodeBitExactly(Vector vector) {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("FFV1"),
      CodecId = "V_FFV1",
      Width = 8,
      Height = 6,
      TimeBase = new Rational(1, 1),
      FrameRate = new Rational(1, 1),
      CodecPrivateData = _Bytes(vector.CodecPrivateData),
    };
    var decoder = Ffv1Decoder.Create(stream);
    var actual = new List<byte>();

    for (var i = 0; i < vector.Packets.Length; ++i) {
      var packet = new CodedPacket(0, _Bytes(vector.Packets[i]), i, i, 1, vector.KeyFrames[i]);
      Assert.That(decoder.TryDecode(packet, out var frame), Is.True, $"{vector.Name}, frame {i}");
      Assert.That(frame.Format, Is.EqualTo(vector.Format), $"{vector.Name}, frame {i} format");
      actual.AddRange(frame.PixelData);
    }

    Assert.That(actual, Is.EqualTo(_Bytes(vector.ExpectedRaw)), vector.Name);
  }

  private static IEnumerable<TestCaseData> _Vectors() {
    foreach (var vector in _DATA)
      yield return new TestCaseData(vector).SetName($"FFmpegVector({vector.Name})");
  }

  private static byte[] _Bytes(string base64)
    => base64.Length == 0 ? [] : Convert.FromBase64String(base64);

  private static readonly Vector[] _DATA = [
    new(
      "v0_rice_small",
      PixelFormat.Gray8,
      "",
      ["8vwWxgblw6Iq+lfdgmZ8NM2aAARcAAiYQGEAAjYckquAA1YAAQQABnh/+A=="],
      [true],
      "TEyWlh0daWlMTJaWHR1paUxMJSUHBxoaTEwlJQcHGhpMTCUlBwcaGkxMJSUHBxoa"),
    new(
      "v1_range_default",
      PixelFormat.Gray8,
      "",
      ["hh1INQeUTUvEic6/R2sDch7Hjx2hfSD5+VCYk9QahS9imb/+gg=="],
      [true],
      "TEyWlh0daWlMTJaWHR1paUxMJSUHBxoaTEwlJQcHGhpMTCUlBwcaGkxMJSUHBxoa"),
    new(
      "v3_rice",
      PixelFormat.Gray8,
      "ViuDGeb3k5IduKh6P42iPURkfFy6ZVMNU8J+W3FCb7x2ajmncJvMFBp5",
      ["/BWAAARcAAiYZJwAGrCAAAAPAAx0tR493oAAAWwACNkkjsAAZ4AAAA8AKnFZi5wckwAEXAAEKck+AAALANDKPqEQvz0JAABuST4AAAkA7bF2YQ=="],
      [true],
      "TEyWlh0daWlMTJaWHR1paUxMJSUHBxoaTEwlJQcHGhpMTCUlBwcaGkxMJSUHBxoa"),
    new(
      "v3_range_large",
      PixelFormat.Gray8,
      "VgAv08gYzgnrf2gj0EbCRCgKOCBBHI/9C9eg3X3H4r4WmbHgtt+71wQz",
      ["9L9OsGSh2QusXgAACgB/kIb0N9nRm5YQjIVP7wAACgAT/TupljqD1Ba6/gAABwCUXCV1Dci0Hpq5AAAGAP6zpYM="],
      [true],
      "TEyWlh0daWlMTJaWHR1paUxMJSUHBxoaTEwlJQcHGhpMTCUlBwcaGkxMJSUHBxoa"),
    new(
      "v3_range_custom",
      PixelFormat.Gray8,
      "VgYeL/24RtdwlzABhgHu4RzmvUVNtTgndNguEH11xarjPWAs2IaD/ltHsQ/FXvF7/4F4tX3BISIhvhnGUdkxDwi2f3qyydzKWZ7H3f3jMwfHh93udIfiZcT7/NTAHM0l5GhK1w7fGlI1vlZn/jBvvpbFYAI4TC4XwjNNPWOgedEwClBInbq0bd8gTONlp6IhneeZMhvdx+pLc2TRAKkVKXJr07pXatcaNIp8u8YMydUBHpuUwbxvpU5CcYFutQ==",
      ["78rGFG+b0jeXAAAJAMNmoGw9sSYi5RSBfdMbAAAKAJiiLbKUSS8jM7t+AAAHAImmHw0Z+SvXWGcAAAYAO+0hJA=="],
      [true],
      "TEyWlh0daWlMTJaWHR1paUxMJSUHBxoaTEwlJQcHGhpMTCUlBwcaGkxMJSUHBxoa"),
    new(
      "v3_gop",
      PixelFormat.Gray8,
      "VgAv08gYzgnrf2gj0EbCRCgKOCBBHI/9C9eg3X3H4r4WmbHgtwZanH4J",
      [
        "/BZnrtLJfFHZAAAJAG16+T0930xI822yundRAAAKAPysPnucHV4Xm5z6AAAHAHNOtUAQv54NKdIAAAYAEe+01A==",
        "fJZZ70aONM41AAAJAAimeEs931EpXGjksKUAAAkAgUGO1JwdSZhOsNkAAAcAu5MKbhC/ky6YowAABgBCyY6W",
        "fJZP/gKAJH0AAAgAWuwD2D3fVi2OxAKlAAAACQDcyFaLnB03DNVKOQAABwDCEdWbEL+KAnycAAAGAKmsqno=",
        "/BZnrtLJfFHZAAAJAG16+T0930xI822yundRAAAKAPysPnucHV4Xm5z6AAAHAHNOtUAQv54NKdIAAAYAEe+01A==",
      ],
      [true, false, false, true],
      "TEyWlh0daWlMTJaWHR1paUxMJSUHBxoaTEwlJQcHGhpMTCUlBwcaGkxMJSUHBxoaTEyWlh0daWlMTJaWHR1paUxMJSUHBxoaTEwlJQcHGhpMTCUlBwcaGkxMJSUHBxoaTEyWlh0daWlMTJaWHR1paUxMJSUHBxoaTEwlJQcHGhpMTCUlBwcaGkxMJSUHBxoaTEyWlh0daWlMTJaWHR1paUxMJSUHBxoaTEwlJQcHGhpMTCUlBwcaGkxMJSUHBxoa"),
    new(
      "v3_gray10",
      PixelFormat.Gray10,
      "VgA+yVGOJrXd0wZq2dtmd58aWIG4TJkxH+AxG3hLdwknUz13o2yRRSspP5fNK4UN+i/nqg==",
      ["/BZqKr3TKXxP8BYAAAsAqXFQET3fUgryR9R8117RvQAADABNKRP9nB1gRQ7wFXoAAAgA/hpMkxC/qiXaNvIAAAcA3KJRyQ=="],
      [true],
      "LwEvAVkCWQJ0AHQAowGjAS8BLwFZAlkCdAB0AKMBowEvAS8BlQCVABwAHABmAGYALwEvAZUAlQAcABwAZgBmAC8BLwGVAJUAHAAcAGYAZgAvAS8BlQCVABwAHABmAGYA"),
    new(
      "v3_yuv12",
      PixelFormat.Yuv420P12,
      "VgBNzVsvbVPyfjzgMRt4S3cJJ1NBqVKkrZomfW5mS4xurHolD2UcwNThL2mWoGWsPfQOBg==",
      ["9L9PfcuNMZDcGMEws6oiKoO4zAo0qkMjwAmuggAAHAC2OKOtN9nUXdq5sSFs7tBPkG50gbajJDtb7CZGznx0eF9aDw01SA0AACMAvW/dyZY6hO3hrljo7k2ohjksNjAqMET0AAAUAPbOIO8NyLy+r2A08rz613wirtucGOhHAAATAMLVdhM="],
      [true],
      "EAUQBRAJEAmQApACoAagBhAFEAUQCRAJkAKQAqAGoAYQBRAFAAMAA2ABYAFgAmACEAUQBQADAANgAWABYAJgAhAFEAUAAwADYAFgAWACYAIQBRAFAAMAA2ABYAFgAmACoAVgAwAPoAygBeAGwAkgCaAF4AbACSAJAA8gAuAG4A0AD5AGwAdwCQAPkAbAB3AJ"),
    new(
      "v3_rgb16",
      PixelFormat.Rgb48,
      "VfZpleDGXqPXIjZcsCouYS+/yTxvKXDhmN1CjzaT1ODYG1OgriwLJ5xwUFe2HVg7Ze4fPw==",
      ["/BZq+mZNdG+KyUkHApZIj94I7W+0D86naeOb/P7JD6TsblnY+G+pmNok+MNUY4dFfcV049PDkKLuL8XagZBB4gvtm70OzXNj8sWK//UAAE0AB2zXBj3fU/olSsdTtbvrEuFwPDjsZpcPxzHFj9NwY1l65oUSek8ndN5eFYneU1Vh3f7UKL3eP5z3zhRTq1T8jLvM9ueZJee+9yJvPgKs/7gbs/0h7nvyJB8AAFUABFNRlZwdYPsC5bW6WbjB0eLG3/kyBFuuflnz3UP62Q7RCNRk/jiKZnxvtEb/5QAAKgC6nDiEEL+udjYSK/v52mcXzpf+OS/3+dGh7AcPzauM6Zhsr0WyZC0rONdNZ//9AAAqAF9JExk="],
      [true],
      "//8AAAbQt1MmzAAAR03YWBClGmvif1IYAAAc/63hJwwAAO512CkP3v///tQAAP4T//8AAAbQt1MmzAAAR03YWBClGmvif1IYAAAc/63hJwwAAO512CkP3v///tQAAP4T//8AAAAAz9YVkQfDLyko9wAABxw3xhUGAAAGgytlCewAADsaNQEDxUJKPlQAAD3t//8AAAAAz9YVkQfDLyko9wAABxw3xhUGAAAGgytlCewAADsaNQEDxUJKPlQAAD3t//8AAAAAz9YVkQfDLyko9wAABxw3xhUGAAAGgytlCewAADsaNQEDxUJKPlQAAD3t//8AAAAAz9YVkQfDLyko9wAABxw3xhUGAAAGgytlCewAADsaNQEDxUJKPlQAAD3t"),
  ];

  public sealed record Vector(
    string Name,
    PixelFormat Format,
    string CodecPrivateData,
    string[] Packets,
    bool[] KeyFrames,
    string ExpectedRaw);
}
