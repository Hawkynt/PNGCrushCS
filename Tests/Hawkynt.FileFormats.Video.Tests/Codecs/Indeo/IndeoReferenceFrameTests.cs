using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace FileFormat.Codecs.Indeo.Tests;

/// <summary>
/// Three real frames of each format, decoded and checked against what ffmpeg makes of the same bytes.
/// </summary>
/// <remarks>
/// <b>What these are.</b> The first three coded frames of two clips from <c>samples.ffmpeg.org</c> —
/// <c>V-codecs/IV41/indeo41.avi</c> at 320x240 and <c>V-codecs/IV50/girl_01.avi</c> at 256x256 — as
/// the packets a container hands a decoder, each with its length in front of it. They are here rather
/// than in a file because they are small and because the point of them is the bytes, not the
/// container they came out of.
/// <para/>
/// <b>What the hashes are.</b> The MD5 of each decoded picture's three planes end to end, luminance
/// then blue-difference then red-difference, produced by
/// <c>ffmpeg -i &lt;clip&gt; -frames:v 3 -pix_fmt yuv410p -f rawvideo</c> with ffmpeg 9.0.1. They are
/// captured output and not a design: a decoder that agrees with them read the bitstream the way the
/// only other implementation of these formats reads it.
/// <para/>
/// Three frames because the first is intra and the two after it are predicted, so a decoder that got
/// the reference buffers or the motion compensation wrong fails on the second even where the first is
/// perfect. The whole corpus was compared frame by frame the same way — 17 261 Indeo 4 pictures over
/// five clips and 5 516 Indeo 5 pictures over fifteen, every sample of every plane identical, as
/// `codec-notes.md` records — and these six frames are what is small enough to keep.
/// </remarks>
[TestFixture]
public sealed class IndeoReferenceFrameTests {

  /// <summary>The first three coded frames of <c>V-codecs/IV41/indeo41.avi</c>.</summary>
  private const string _INDEO4_PACKETS =
    "7gQAAPj/g+wEAIIPAACCT0QPfwCACgBFLIGDQIsA/qMMAACK4liaogGcozmKYjie4ziapnmepSCapmkEoyiOpUmCYQAeYViOQQGQp3AA5iGO" +
    "pQGWwAkA5igCQAiAhzkIREgYQHkehxmOhFkIoGiIpDkWZzGMwjGKojgEonEWJ3AaJGCMR0iSQwCQRCCa4ziUo2ie4jiOoniOonge41mKhzmO" +
    "5Fme5SGSx2CKoniapziKpSmK5iiep3maJXgCBQAKBjkUQxmMBzAap2kOwACOp3gYo0CKYVmYpHCcpnieYyCapkiaImmOpGiQoVAAp0gGoxiQ" +
    "oyiOIjiK42mO42mG4imWp3nBhxm6OruapquzbTvbrh66Oruapm2bpquzq+1sujq7Ors6uzrbtrNtm6Ztm6ars23bzrbtbNvOtm2atm2atm2a" +
    "rqbpapqupulqmq7OrqZt2qZt2qbpapqupmmbpm3apqtp2qZt2qZt2qars23apm3apm3apm3apm3atm2atmmbtmmbtmmbtu1s2862aZu2aZu2" +
    "adumadqmbdqmbdqmbdqmbdqmbdqmbdqmbdrOrqZt2qZt2rZpm7Zp2rZpm7Zpm7bpg7Zpm7Zpmz5om7Zpm7Zpupq2aZs+6Gqapm3apm3apm3a" +
    "pm3apm3apm3apm26mqZt2qZt2rbtbLqapsfOpqvp6my6Oruatu1sutrOqW2mpquzbZumq7OraZumbZumq2mbtmm62s62s20727Zpupqmbdq2" +
    "s23atmnatrNtupqmbZumbZumq2natrNt2qZt2qbti6Ztujq7OnvsbLrazq6ms+1qmrZpupq2advOrqZpO7s6e2yapqtpm7ZtmqbHpmnbHtq2" +
    "bTrbtmmbpqtpm7Zpm7Zp2rZpm7Zpm6Ztm7Zpm7ZpupqmbTvbtrNtO7uapm3atml6bDrbpm2btmm6Otu2aTvbpm0727azbZu2aTvbtrNtO9u2" +
    "s20727Zp2rZpupqmbdq2abo626ars20726araZumbdqmbbo626Zt2qZt2raznZq2aZu2aZu2aZu2aZu2q2napm3apquzq2natoe27Wy7mqZt" +
    "e+hqm86uzrars23atmnapqtp2qZp26Zp2862AYEKAAUA8GHViwA2AQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIITAAXj92HVu0gwoERgYeBwwAL+lwQAAIqCKKiqKoAACgoiCIiIIgKqoKCgoqoq" +
    "KIqCAiqggCoKIKqqqogKqoqAgIiAACKggggqgKqgqIgIAKCiqIqiCqqqCiKqKKKqKqqqqmjKytr1Ae3aVRSUFU27ioKdqampqSkqKD6voGga" +
    "dho0KCro24oKCooeFBSDgmJQUAwKigqK+oCea1BUeF5FQ1BWVlFUUFRwnSsodoWoqEHn2hWiomvQubKymgY1ZWU1NVdWVlCMCtoVo84VoqIG" +
    "7QqK2hUUNaip6YPq2rUrKCooRmUFRe0KikFZ58pqBip6rqKsQU1UVLRrUFTQt/VMz9QUguKZmpqampq6mpqampqygqKKinZQUVFRUVHRoKJi" +
    "CooKimdqamoKikFNQVFNISgqKKqoKQ0KVmVyIDQuMTEuMTUuNjANCgAAAAAAAAAAAAAAwgMAAPj/k8ADAIIfAACCUWRGfgCACAAFSgA0Av6r" +
    "CgAAFBqFQqFQKBQKhUKhMCgUCoUCoVBYEAoNQqFQaAgKDcTgUTAgAgCEImBwDAYMQAERMBgYAEDjUBgwDAzDY1BQAAoPQAFQKAAWh0MB0HAY" +
    "AIsG4DFgAAwARCJgMAAMBsADMEA8DgzEwQBgGBoHg+EBABgGBkMBQRgMBoUC4WBwLAKERCKwCAAQikSAkEgEGIiAAQB4AAYAAEAQADQOiwPB" +
    "UCg4GIdEAQAYFAqKRcBQSDQAgAEQAQOAOAAAjAFgAAAqAAAOicRBAXA0EoRE4wFABASFRwORKBwKhQbjUCg0CIdC41AIDACIBsARQDwSicQB" +
    "AAAMBoiE4lFAPAoOwsPhaDAMB8Zg0SjBhxvapqtp2qZt2qZt2qZt2qZtujrbpm3apm3apm3apm3apm3apm3apm3apm3apm3apm3apm3apm3a" +
    "pm3apm3apm3apm2atmnbzrZt2qZp26Zpm7Zt2qZt2qZt2qZt2qZt2qZt2qZt2qZt2qZtmrZt2qZtmrZtmrZp28626WqarqZpm7Zt2qZp26Zp" +
    "26Zp26Zt2qZt2qZt2qZt2qZt2qZt2qZt2qZt2qZtmrZpm7Zpm7Zpm7ZtmrZpm7Zpm7Zpm7Zpm7Zpm7Ztmq6mbdqmbdqmbdqmbdqmbdqmbdqm" +
    "bdqmbZq2aZu2bZq2aZu2aZu2aZu2bZq2bdqmbdqmbdqmbdqmbdqmbdqmbdqmbdqmbdqmbZq2aZumbdqmbZumbdqmbZu2aZu2aZu2aZu2aZu2" +
    "aZu2aZu2aZu2aZu2aZu2adqmbdqmbTvbpm3apm3atmnapm3apm26mqZt2qZtm6Zt2rZpm7Zpm7Zpm7Zpm7Zpm7Zpm7Zpm7Zpm6aradq2abqa" +
    "pm2btrNtupqmbTvbtmnapm3atrNtm6Ztm7azbZumbRqBCAAFAHAyAjYBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
    "AAAAAAAAAAAAAAAABAAACAAAAAAAAAAAAAAAAAAAAAAAAAAAgg0AZfx3MqLAgAMBAlYCAAAAAAAgAAAAAACAAAICAAAAIAAooCAIAAAAoAAA" +
    "IqACKIgAAAACAAqqoAiqAgAAAAAKiqIiWgAAAAAARVFVVFUAAAAAAABVEFVF2Yd92IedbdM2bdM2LU3btF1N23S1Tdu0Tdu0XV1N27RNW03T" +
    "0rRN27Q0bVNtdXZ2Nm011VZnZ2dn01bTVtNW09l2Nm01bWcDAAAAAAAAAAAAMgYAAPj/izAGAIIvAACCTbyEeQCAFADdjAPzUWKKriwOxEzR" +
    "+OgEAf47EwAANACIR6GQAAAajwMAUBgYAoQC4OF4FBIBAGKwOAAAiAaigSgwGAGHQqEwOBqNRoCgYCgEAQSAwQAABgAAABFgJAIAAAMAMBwe" +
    "hkQCsHgQHAQBgGBwABiBQQDRaDwajQCAUEgkHgGH4/AAAACPR+HxeDQaj8aj8Xg8Hg1E4PFoPB6Px+PxeDwej8fjcSgUHo/H4/F4PB6Px+Px" +
    "eDweiERgAAAABgMGYCEwDBYOwIJgADwIhQehATA0GIQGg9AAMACIAuNhKAAKBcOiUBgMFo0AYlAoFIABAIABAFBwDACAAWAAcAAYhYGhUCgU" +
    "Bg0G41AYNAyDgqFQUAwcBoahUCgYCoNCo1BoBArCBxiKsiiLsiiLsiiLsiiLsiiLsiiLsiiLsiiLsiiLsiiLoiyLsiiLsi7LoizKoizKoizK" +
    "oizKoizKoizKoizKoizKoizKoizKoizKoizKoizKoiyKsiiLsijLuizKoiyLsiiLsiiLsiiLsiiLsiiLsiiLsiiLsiiLsiiLsijKHkqOdHuF" +
    "G73eS1hBTj1bFj3GFWCXV7jBD9M7PXUXEzmd6PAKN9jrXcyjmDuWRVmURVmUZVGURVmUPXbXHZ2jXcuiKIuy73ZefYSe6RrO6dJ9+Fjo+zFH" +
    "3A/qlx8IXXfOfNRjRZ8OBofmtr9Az4wHCNwAOgwHxz4GJpR9Mq43eu0g+6nKfoqD6MNlnA+wl+d+bHJmidD9uP0oyMS54Bj28Vz200wXu3ok" +
    "+riHOE6e+45J+ngu+1HOBR1mop+qC3Jl7ousZD942U9xkJ1I9FP12s4DQAeu83C/1xocZI+PbwF8oF46WM5dQZJ93E/TN57P9XSPocfLouxA" +
    "Tih7bOXbPus4HeC9uqd/7QN9+p7CgaLHF8x51I2YegRXUfAguw4HDt4A63BmjaL8jEU/+OIJ9/usn7jXxoVMl3PxwZ696x4aR9B90MMejnbr" +
    "G1xZPmDfsfvgo3zUQ324XezZAEf6sftm5eRes3GRH70/6G4n+gbngKmHbpsn2Gm/iU7Y2FMx3aWnxwOTcaGngLkhSXZdPlbfGzD6bPxY/eAn" +
    "+lHImV2Xl914kD3sE+Q99ljvbDjAXuNB9imuLH3RO5tP9mGPLZiGHu6JsiiLsiz6YOeJPuzCwexH6etg2j5Mx9vuTszox+/76dzSEYvRlQDR" +
    "j9/Dn533e+e2m3Oepn6UPqTf4t7Uw7eHHu9n6X4+xNTXXMwOz90nwMyuI+AJfQMDJzpwMNkPhYOc+om7YDHYw/348RGWDt0PT4A99tx9QZLs" +
    "8WWYD17oZWxGj/fTnODU1SPdD06DJ3vouXstI9MBOMo+fO6eBoA+WYfpE/YSOLGfqoe4mH3AkakNpg+fu6cBoLfGK8PUW30cIycLLEaHFW/7" +
    "rJ+hh267pxZyni70aRcP9omuZVEWZVEWZVGUZVEWZVEWZVEWRVkWZVEWZVEWZVHWZVmURVkUTVEWZVEWZVEWZVEWZVEWZVE0RVmURVmURVmU" +
    "RVkUTVEWZVEWZVE0RVEWTVEWZVGURVEWZVGWdVmURVk0RVmUdVOURVmURVmURVmURVmURVmURVmURVmXZVEWZVEWZVGURVmURVMUZVkUZVEW" +
    "ZVEWZVEWZVEWTVGURVmUZVGUBYEIAAUA8DECNgEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
    "AAAAAAAAAAAQAAAAAAAAAAAAAAAAAACCDwDl9/fx4oDBz4HFgANKAwAAAAAAAAAoiAAAAAAAAKqgiqoAAAAAAACCqiiKqKKqqqIqKIAqqoqi" +
    "IigoCACoIoAKAAAoAAAAAICiCioKAAgAAAAAAAIiqqCqIqigD2hQV2RldXUFVjxTYHV1dUVWUAwKQdH0AX1AQVH5Rl1BUUFdkRVUFBQVlBUU" +
    "g7KygmJQVlBRUFBWUVBU0MCKChpAUYM2RVNQ1K5du3bt2rXfKCgqKCqYsqKCsqKCsrKysmJQUDQFRUUFfUtZ0RRM8UaBz9QV9S31M31LwdTP" +
    "FE1dXQEAAAAAAAAAAAAA";

  /// <summary>What ffmpeg decodes those three frames to, one MD5 of Y, U and V per frame.</summary>
  private static readonly string[] _Indeo4Hashes = [
    "c8d5d13ec511a1f627f3616a5512b8e5",
    "41e86e18adde6656cbde82b3ed7f439b",
    "d114c5b573c6de1189e5290cfd098b33",
  ];

  /// <summary>The first three coded frames of <c>V-codecs/IV50/girl_01.avi</c>.</summary>
  private const string _INDEO5_PACKETS =
    "XAEAAB8AgQwAeIAAEIIDDzYQ1VwBAAAAAMCtAABAAAABlgICAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAWMDwkoAAEAAAAEKAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADCSgAAQAAAAQoBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFwBAAAfAIEMAHiAABCCAw82ENVcAQAAAADArQAAQAAAAZYCAgAAAAAAAAAAAAAAAAAAAAAA" +
    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFjA8JKAABAAAABCgEAAAAAAAAA" +
    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAwkoAAEAAAAEKAQAAAAAAAAAAAAAAAAAA" +
    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAC8AQAAHwCBDAB4gAAQggMPNhDVvAEAwCAA" +
    "wA0BAHxBiGFgEAH+CwQAAAIAAAAAAEAAAAAAAAAAAAA4DQAAAAAAAAAACg8AAAAAAAAAwBsBAAAAAAAAAEADLwAAAAAAAADAAw4AAAAAAAAA" +
    "wEMBAAAAAAAAAEAwAQAAAAAAAAAQAA0AAAAAAACAAAADAAAAAAAABNhLAQAAAAAAAPeAcwAAAAAAAADzgnkBAAAAAAAA8AJ4AQAAAAAAAHAC" +
    "eAEAAAAAAAD3AHANAABnc4ApyxRbohLRqEQN29BliW1oxYG2oVGpaMWWqFS0olFDI7ZhR6KGRg0H2JJYKhWNGrZUdIpt2IZGbKkaGtGoqGEb" +
    "GjU0olHD1IptaMQWsQ1bqlLRxAFqqExbogDCSgAAQAAAAQoBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
    "AAAAAAAAAAAAAAAAAAAAAAAAAMJKAABAAAABCgEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
    "AAAAAAAAAAAAAAAAAAAAAAAA";

  /// <summary>What ffmpeg decodes those three frames to, one MD5 of Y, U and V per frame.</summary>
  private static readonly string[] _Indeo5Hashes = [
    "c74b3a1c1659a04d99dfb0b909a2ca8c",
    "c74b3a1c1659a04d99dfb0b909a2ca8c",
    "c44eaaab7e9056158297e99e79f28252",
  ];

  [Test]
  [Category("Unit")]
  public void Indeo4DecodesItsFramesExactlyAsFfmpegDoes()
    => _Check(new Indeo4Decoder(), _INDEO4_PACKETS, _Indeo4Hashes, 320, 240);

  [Test]
  [Category("Unit")]
  public void Indeo5DecodesItsFramesExactlyAsFfmpegDoes()
    => _Check(new Indeo5Decoder(256, 256), _INDEO5_PACKETS, _Indeo5Hashes, 256, 256);

  private static void _Check(IviDecoder decoder, string packets, string[] expected, int width, int height) {
    var pictures = new List<IviPicture>();

    foreach (var packet in _Packets(packets)) {
      var picture = decoder.Decode(packet);
      if (picture != null)
        pictures.Add(picture);
    }

    Assert.That(pictures, Has.Count.EqualTo(expected.Length));

    for (var i = 0; i < expected.Length; ++i) {
      var picture = pictures[i];
      Assert.That(picture.Width, Is.EqualTo(width));
      Assert.That(picture.Height, Is.EqualTo(height));
      Assert.That(_Hash(picture), Is.EqualTo(expected[i]), $"frame {i}");
    }
  }

  /// <summary>The three planes end to end, hashed the way the reference output was.</summary>
  private static string _Hash(IviPicture picture) {
    var frame = new byte[picture.Luma.Length + picture.ChromaBlue.Length + picture.ChromaRed.Length];
    picture.Luma.CopyTo(frame, 0);
    picture.ChromaBlue.CopyTo(frame, picture.Luma.Length);
    picture.ChromaRed.CopyTo(frame, picture.Luma.Length + picture.ChromaBlue.Length);

    return Convert.ToHexString(MD5.HashData(frame)).ToLowerInvariant();
  }

  /// <summary>Unpacks the length-prefixed packets the constants above hold.</summary>
  private static IEnumerable<ReadOnlyMemory<byte>> _Packets(string encoded) {
    var data = Convert.FromBase64String(encoded);
    var at = 0;

    while (at < data.Length) {
      var length = BitConverter.ToInt32(data, at);
      at += 4;
      yield return data.AsMemory(at, length);
      at += length;
    }
  }
}
