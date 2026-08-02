using System;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Jpeg.Tests;

/// <summary>A four-component JPEG: CMYK, or YCCK once its colour part is turned back into ink.</summary>
/// <remarks>
/// These were read as though they were three-component files — the first three planes went through
/// the YCbCr conversion and the black plane was dropped — so what came out was the negative of the
/// picture. The red quadrant of the image below arrived as cyan.
///
/// The three colour planes carry ink, where more means less light; the key plane carries the light
/// that is left. Taking all four the same way is what turned the picture inside out.
/// </remarks>
[TestFixture]
public sealed class JpegFourComponentTests {

  /// <summary>A 40x24 YCCK JPEG of four quadrants — red, green, blue, white — written by ImageMagick.</summary>
  private const string _CmykQuad =
      "/9j/7gAOQWRvYmUAZAAAAAAC/9sAQwACAQEBAQECAQEBAgICAgIEAwICAgIFBAQDBAYFBgYGBQYGBgcJCAYHCQcGBggLCAkK"
      + "CgoKCgYICwwLCgwJCgoK/9sAQwECAgICAgIFAwMFCgcGBwoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoK"
      + "CgoKCgoKCgoKCgoK/8AAFAgAGAAoBAERAAIRAQMRAQQRAP/EABcAAQEBAQAAAAAAAAAAAAAAAAAJCAr/xAAxEAABAAIMDwAA"
      + "AAAAAAAAAAAAEhQKERYYGWJmaKWm4+QDBwkVFyEmQURGSIWGxMX/xAAZAQEAAwEBAAAAAAAAAAAAAAAACAkKBgf/xAA7EQAB"
      + "AAMJCBMAAAAAAAAAAAAAEqTkCBETFBUYGWVmBkJERmKjwuMCBAcJFyEiIyZBQ2FkgoOEhcTF/9oADgQBAAIRAxEEAAA/ANmE"
      + "rDP+X8AAM15QzlDuHrHqW5rhXp6ZetvKuPnxf6IM1nqResAACwz1WXlF2pQLSbWTX2My+0eNplJrAeqy8ou1FJtZNfYxR42m"
      + "UmsE02RBjpeC6INmnWOsdBxigqqubYmGTSWIrSG9vVKlzM7n4SJW6PwEBAYWmknDeG2DzyHe+/1Pcc1nHtzMy2W+dlWVYtex"
      + "WCisYytsJpxjIRQvkuSJpwvs3mtt0JUzmKqz+pJrTwqkWWcCF9m81tugnMVVn9SJ4VSLLODq2Mq5H0AAEO2Zn03+YfELAHC+"
      + "MPtPsnK3Tdl5tEEOywA5UAAH/9k=";

  [Test]
  [Category("Unit")]
  public void A_four_component_file_decodes_to_the_colours_it_was_made_from() {
    var file = JpegReader.FromSpan(Convert.FromBase64String(_CmykQuad));
    var rgb = JpegFile.ToRawImage(file).ToRgb24();

    byte At(int x, int y, int channel) => rgb[(((y * file.Width) + x) * 3) + channel];

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(40));
      Assert.That(file.Height, Is.EqualTo(24));
      Assert.That((At(10, 6, 0), At(10, 6, 1), At(10, 6, 2)), Is.EqualTo(((byte)255, (byte)0, (byte)0)), "top left is red");
      Assert.That(At(30, 6, 1), Is.GreaterThan((byte)200), "top right is green");
      Assert.That(At(10, 18, 2), Is.GreaterThan((byte)200), "bottom left is blue");
      Assert.That(At(30, 18, 0), Is.GreaterThan((byte)200), "bottom right is white");
      Assert.That(At(30, 18, 2), Is.GreaterThan((byte)200), "bottom right is white");
    });
  }
}
