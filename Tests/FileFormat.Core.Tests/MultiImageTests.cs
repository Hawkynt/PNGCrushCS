using System;
using System.Collections.Generic;
using FileFormat.Ani;
using FileFormat.Apng;
using FileFormat.BigTiff;
using FileFormat.Core;
using FileFormat.Cur;
using FileFormat.Dcx;
using FileFormat.Fli;
using FileFormat.Ico;
using FileFormat.Icns;
using FileFormat.Jpeg;
using FileFormat.Mng;
using FileFormat.Mpo;
using FileFormat.Pcx;
using FileFormat.Png;
using FileFormat.Tiff;

namespace FileFormat.Core.Tests;

[TestFixture]
public sealed class MultiImageTests {

  #region Helpers

  private static byte[] _CreateSmallPngBytes(int width, int height) {
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 37 % 256);

    var pngFile = new PngFile {
      Width = width,
      Height = height,
      BitDepth = 8,
      ColorType = PngColorType.RGB,
      PixelData = _SplitIntoRows(pixelData, width * 3, height),
    };
    return PngWriter.ToBytes(pngFile);
  }

  private static byte[][] _SplitIntoRows(byte[] data, int stride, int height) {
    var rows = new byte[height][];
    for (var y = 0; y < height; ++y) {
      rows[y] = new byte[stride];
      data.AsSpan(y * stride, stride).CopyTo(rows[y].AsSpan(0));
    }
    return rows;
  }

  private static IcoFile _CreateIcoFileWithPngEntries(int entryCount, int size = 16) {
    var images = new IcoImage[entryCount];
    for (var i = 0; i < entryCount; ++i)
      images[i] = new IcoImage {
        Width = size + i * 16,
        Height = size + i * 16,
        BitsPerPixel = 32,
        Format = IcoImageFormat.Png,
        Data = _CreateSmallPngBytes(size + i * 16, size + i * 16),
      };
    return new IcoFile { Images = images };
  }

  private static byte[] _CreateSmallJpegBytes(int width, int height) {
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var raw = new RawImage { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixelData };
    var jpegFile = JpegFile.FromRawImage(raw);
    return _ToBytes(jpegFile);
  }

  private static byte[] _ToBytes<T>(T file) where T : IImageFormatWriter<T>
    => T.ToBytes(file);

  private static PcxFile _CreateSmallPcxFile(int width, int height) {
    var raw = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[width * height * 3],
    };
    return PcxFile.FromRawImage(raw);
  }

  private static int _ImageCount<T>(T file) where T : IMultiImageFileFormat<T>
    => T.ImageCount(file);

  private static RawImage _ToRawImage<T>(T file, int index) where T : IMultiImageFileFormat<T>
    => T.ToRawImage(file, index);

  private static IReadOnlyList<RawImage> _ToRawImages<T>(T file) where T : IMultiImageFileFormat<T>
    => T.ToRawImages(file);

  #endregion

  #region FormatCapability.MultiImage

  [Test]
  [Category("Unit")]
  public void FormatCapability_MultiImage_HasValue16()
    => Assert.That((int)FormatCapability.MultiImage, Is.EqualTo(16));

  [Test]
  [Category("Unit")]
  public void FormatCapability_MultiImage_IsFlagValue() {
    var value = (int)FormatCapability.MultiImage;
    Assert.That(value & (value - 1), Is.EqualTo(0), "MultiImage should be a single-bit flag");
  }

  #endregion

  #region IcoFile multi-image

  [Test]
  [Category("Unit")]
  public void IcoFile_ImageCount_ReturnsTwoForTwoEntries() {
    var ico = _CreateIcoFileWithPngEntries(2);
    Assert.That(_ImageCount(ico), Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void IcoFile_ImageCount_ReturnsZeroForEmpty() {
    var ico = new IcoFile { Images = [] };
    Assert.That(_ImageCount(ico), Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void IcoFile_ToRawImage_Index0_ReturnsDimensionsOfFirstEntry() {
    var ico = _CreateIcoFileWithPngEntries(2);
    var raw = _ToRawImage(ico, 0);
    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(16));
      Assert.That(raw.Height, Is.EqualTo(16));
    });
  }

  [Test]
  [Category("Integration")]
  public void IcoFile_ToRawImage_Index1_ReturnsDimensionsOfSecondEntry() {
    var ico = _CreateIcoFileWithPngEntries(2);
    var raw = _ToRawImage(ico, 1);
    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(32));
      Assert.That(raw.Height, Is.EqualTo(32));
    });
  }

  [Test]
  [Category("Integration")]
  public void IcoFile_ToRawImages_ReturnsTwoItems() {
    var ico = _CreateIcoFileWithPngEntries(2);
    var images = _ToRawImages(ico);
    Assert.That(images, Has.Count.EqualTo(2));
  }

  [Test]
  [Category("Integration")]
  public void IcoFile_ToRawImages_EachItemHasValidPixelData() {
    var ico = _CreateIcoFileWithPngEntries(2);
    var images = _ToRawImages(ico);
    Assert.Multiple(() => {
      Assert.That(images[0].PixelData, Is.Not.Null.And.Not.Empty);
      Assert.That(images[1].PixelData, Is.Not.Null.And.Not.Empty);
    });
  }

  [Test]
  [Category("Unit")]
  public void IcoFile_ToRawImage_NegativeIndex_Throws() {
    var ico = _CreateIcoFileWithPngEntries(2);
    Assert.Throws<ArgumentOutOfRangeException>(() => _ToRawImage(ico, -1));
  }

  [Test]
  [Category("Unit")]
  public void IcoFile_ToRawImage_IndexEqualToCount_Throws() {
    var ico = _CreateIcoFileWithPngEntries(2);
    Assert.Throws<ArgumentOutOfRangeException>(() => _ToRawImage(ico, 2));
  }

  [Test]
  [Category("Unit")]
  public void IcoFile_ToRawImage_IndexBeyondCount_Throws() {
    var ico = _CreateIcoFileWithPngEntries(1);
    Assert.Throws<ArgumentOutOfRangeException>(() => _ToRawImage(ico, 5));
  }

  [Test]
  [Category("Unit")]
  public void IcoFile_Capabilities_IncludesMultiImage() {
    var caps = _GetCapabilities<IcoFile>();
    Assert.That(caps.HasFlag(FormatCapability.MultiImage), Is.True);
  }

  #endregion

  #region CurFile multi-image

  [Test]
  [Category("Unit")]
  public void CurFile_ImageCount_ReturnsTwoForTwoEntries() {
    var pngData = _CreateSmallPngBytes(16, 16);
    var cur = new CurFile {
      Images = [
        new CurImage { Width = 16, Height = 16, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = pngData, HotspotX = 0, HotspotY = 0 },
        new CurImage { Width = 16, Height = 16, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = pngData, HotspotX = 8, HotspotY = 8 },
      ]
    };
    Assert.That(_ImageCount(cur), Is.EqualTo(2));
  }

  [Test]
  [Category("Integration")]
  public void CurFile_ToRawImage_Index0_ReturnsValidImage() {
    var pngData = _CreateSmallPngBytes(16, 16);
    var cur = new CurFile {
      Images = [
        new CurImage { Width = 16, Height = 16, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = pngData, HotspotX = 0, HotspotY = 0 },
      ]
    };
    var raw = _ToRawImage(cur, 0);
    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(16));
      Assert.That(raw.Height, Is.EqualTo(16));
      Assert.That(raw.PixelData, Is.Not.Empty);
    });
  }

  [Test]
  [Category("Unit")]
  public void CurFile_ToRawImage_OutOfRange_Throws() {
    var cur = new CurFile { Images = [] };
    Assert.Throws<ArgumentOutOfRangeException>(() => _ToRawImage(cur, 0));
  }

  #endregion

  #region AniFile multi-image

  [Test]
  [Category("Unit")]
  public void AniFile_ImageCount_ReturnsFrameCount() {
    var ico = _CreateIcoFileWithPngEntries(1);
    var ani = new AniFile {
      Header = new AniHeader(AniHeader.StructSize, 2, 2, 16, 16, 32, 1, 10, 0),
      Frames = [ico, ico],
    };
    Assert.That(_ImageCount(ani), Is.EqualTo(2));
  }

  [Test]
  [Category("Integration")]
  public void AniFile_ToRawImage_Index0_ReturnsValidImage() {
    var ico = _CreateIcoFileWithPngEntries(1);
    var ani = new AniFile {
      Header = new AniHeader(AniHeader.StructSize, 1, 1, 16, 16, 32, 1, 10, 0),
      Frames = [ico],
    };
    var raw = _ToRawImage(ani, 0);
    Assert.That(raw.Width, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void AniFile_ToRawImage_OutOfRange_Throws() {
    var ani = new AniFile {
      Header = new AniHeader(AniHeader.StructSize, 0, 0, 0, 0, 0, 0, 0, 0),
      Frames = [],
    };
    Assert.Throws<ArgumentOutOfRangeException>(() => _ToRawImage(ani, 0));
  }

  #endregion

  #region DcxFile multi-image

  [Test]
  [Category("Unit")]
  public void DcxFile_ImageCount_ReturnsTwoForTwoPages() {
    var page0 = _CreateSmallPcxFile(8, 8);
    var page1 = _CreateSmallPcxFile(16, 16);
    var dcx = new DcxFile { Pages = [page0, page1] };
    Assert.That(_ImageCount(dcx), Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void DcxFile_ImageCount_ReturnsZeroForEmpty() {
    var dcx = new DcxFile { Pages = [] };
    Assert.That(_ImageCount(dcx), Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void DcxFile_ToRawImage_Index0_ReturnsDimensionsOfFirstPage() {
    var page0 = _CreateSmallPcxFile(8, 4);
    var page1 = _CreateSmallPcxFile(16, 12);
    var dcx = new DcxFile { Pages = [page0, page1] };
    var raw = _ToRawImage(dcx, 0);
    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(8));
      Assert.That(raw.Height, Is.EqualTo(4));
    });
  }

  [Test]
  [Category("Integration")]
  public void DcxFile_ToRawImage_Index1_ReturnsDimensionsOfSecondPage() {
    var page0 = _CreateSmallPcxFile(8, 4);
    var page1 = _CreateSmallPcxFile(16, 12);
    var dcx = new DcxFile { Pages = [page0, page1] };
    var raw = _ToRawImage(dcx, 1);
    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(16));
      Assert.That(raw.Height, Is.EqualTo(12));
    });
  }

  [Test]
  [Category("Integration")]
  public void DcxFile_ToRawImages_ReturnsTwoItems() {
    var dcx = new DcxFile { Pages = [_CreateSmallPcxFile(8, 8), _CreateSmallPcxFile(8, 8)] };
    var images = _ToRawImages(dcx);
    Assert.That(images, Has.Count.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void DcxFile_ToRawImage_OutOfRange_Throws() {
    var dcx = new DcxFile { Pages = [_CreateSmallPcxFile(8, 8)] };
    Assert.Throws<ArgumentOutOfRangeException>(() => _ToRawImage(dcx, 1));
  }

  [Test]
  [Category("Unit")]
  public void DcxFile_Capabilities_IncludesMultiImage() {
    var caps = _GetCapabilities<DcxFile>();
    Assert.That(caps.HasFlag(FormatCapability.MultiImage), Is.True);
  }

  #endregion

  #region MpoFile multi-image

  [Test]
  [Category("Unit")]
  public void MpoFile_ImageCount_ReturnsTwoForTwoImages() {
    var jpeg0 = _CreateSmallJpegBytes(8, 8);
    var jpeg1 = _CreateSmallJpegBytes(16, 16);
    var mpo = new MpoFile { Images = [jpeg0, jpeg1] };
    Assert.That(_ImageCount(mpo), Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void MpoFile_ImageCount_ReturnsZeroForEmpty() {
    var mpo = new MpoFile { Images = [] };
    Assert.That(_ImageCount(mpo), Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void MpoFile_ToRawImage_Index0_ReturnsCorrectDimensions() {
    var jpeg0 = _CreateSmallJpegBytes(8, 8);
    var jpeg1 = _CreateSmallJpegBytes(16, 16);
    var mpo = new MpoFile { Images = [jpeg0, jpeg1] };
    var raw = _ToRawImage(mpo, 0);
    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(8));
      Assert.That(raw.Height, Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Integration")]
  public void MpoFile_ToRawImage_Index1_ReturnsCorrectDimensions() {
    var jpeg0 = _CreateSmallJpegBytes(8, 8);
    var jpeg1 = _CreateSmallJpegBytes(16, 16);
    var mpo = new MpoFile { Images = [jpeg0, jpeg1] };
    var raw = _ToRawImage(mpo, 1);
    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(16));
      Assert.That(raw.Height, Is.EqualTo(16));
    });
  }

  [Test]
  [Category("Integration")]
  public void MpoFile_ToRawImages_ReturnsTwoItems() {
    var mpo = new MpoFile { Images = [_CreateSmallJpegBytes(8, 8), _CreateSmallJpegBytes(8, 8)] };
    var images = _ToRawImages(mpo);
    Assert.That(images, Has.Count.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void MpoFile_ToRawImage_OutOfRange_Throws() {
    var mpo = new MpoFile { Images = [_CreateSmallJpegBytes(8, 8)] };
    Assert.Throws<ArgumentOutOfRangeException>(() => _ToRawImage(mpo, 1));
  }

  [Test]
  [Category("Unit")]
  public void MpoFile_Capabilities_IncludesMultiImage() {
    var caps = _GetCapabilities<MpoFile>();
    Assert.That(caps.HasFlag(FormatCapability.MultiImage), Is.True);
  }

  #endregion

  #region TiffFile multi-page

  [Test]
  [Category("Unit")]
  public void TiffFile_ImageCount_Returns1ForSinglePage() {
    var tiff = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PixelData = new byte[16], ColorMode = TiffColorMode.Grayscale,
    };
    Assert.That(_ImageCount(tiff), Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void TiffFile_ImageCount_Returns2ForOneAdditionalPage() {
    var tiff = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PixelData = new byte[16], ColorMode = TiffColorMode.Grayscale,
      Pages = [
        new TiffPage { Width = 8, Height = 8, SamplesPerPixel = 1, BitsPerSample = 8, PixelData = new byte[64], ColorMode = TiffColorMode.Grayscale },
      ],
    };
    Assert.That(_ImageCount(tiff), Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void TiffFile_ImageCount_Returns3ForTwoAdditionalPages() {
    var tiff = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 3, BitsPerSample = 8,
      PixelData = new byte[48], ColorMode = TiffColorMode.Rgb,
      Pages = [
        new TiffPage { Width = 8, Height = 8, SamplesPerPixel = 1, BitsPerSample = 8, PixelData = new byte[64], ColorMode = TiffColorMode.Grayscale },
        new TiffPage { Width = 2, Height = 2, SamplesPerPixel = 3, BitsPerSample = 8, PixelData = new byte[12], ColorMode = TiffColorMode.Rgb },
      ],
    };
    Assert.That(_ImageCount(tiff), Is.EqualTo(3));
  }

  [Test]
  [Category("Integration")]
  public void TiffFile_ToRawImage_Index0_ReturnsFirstIfd() {
    var tiff = new TiffFile {
      Width = 4, Height = 3, SamplesPerPixel = 1, BitsPerSample = 8,
      PixelData = new byte[12], ColorMode = TiffColorMode.Grayscale,
      Pages = [
        new TiffPage { Width = 8, Height = 6, SamplesPerPixel = 1, BitsPerSample = 8, PixelData = new byte[48], ColorMode = TiffColorMode.Grayscale },
      ],
    };
    var raw = _ToRawImage(tiff, 0);
    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(4));
      Assert.That(raw.Height, Is.EqualTo(3));
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
    });
  }

  [Test]
  [Category("Integration")]
  public void TiffFile_ToRawImage_Index1_ReturnsSecondIfd() {
    var tiff = new TiffFile {
      Width = 4, Height = 3, SamplesPerPixel = 1, BitsPerSample = 8,
      PixelData = new byte[12], ColorMode = TiffColorMode.Grayscale,
      Pages = [
        new TiffPage { Width = 8, Height = 6, SamplesPerPixel = 3, BitsPerSample = 8, PixelData = new byte[144], ColorMode = TiffColorMode.Rgb },
      ],
    };
    var raw = _ToRawImage(tiff, 1);
    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(8));
      Assert.That(raw.Height, Is.EqualTo(6));
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    });
  }

  [Test]
  [Category("Unit")]
  public void TiffFile_ToRawImage_IndexEqualToTotal_Throws() {
    var tiff = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PixelData = new byte[16], ColorMode = TiffColorMode.Grayscale,
    };
    Assert.Throws<ArgumentOutOfRangeException>(() => _ToRawImage(tiff, 1));
  }

  [Test]
  [Category("Integration")]
  public void TiffFile_ToRawImages_ReturnsTwoItems() {
    var tiff = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PixelData = new byte[16], ColorMode = TiffColorMode.Grayscale,
      Pages = [
        new TiffPage { Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8, PixelData = new byte[4], ColorMode = TiffColorMode.Grayscale },
      ],
    };
    var images = _ToRawImages(tiff);
    Assert.That(images, Has.Count.EqualTo(2));
  }

  [Test]
  [Category("Integration")]
  public void TiffFile_ToRawImage_Index0_PixelDataPreserved() {
    var pixels = new byte[] { 10, 20, 30, 40 };
    var tiff = new TiffFile {
      Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8,
      PixelData = pixels, ColorMode = TiffColorMode.Grayscale,
      Pages = [
        new TiffPage { Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8, PixelData = new byte[4], ColorMode = TiffColorMode.Grayscale },
      ],
    };
    var raw = _ToRawImage(tiff, 0);
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void TiffFile_Capabilities_IncludesMultiImage() {
    var caps = _GetCapabilities<TiffFile>();
    Assert.That(caps.HasFlag(FormatCapability.MultiImage), Is.True);
  }

  #endregion

  #region BigTiffFile multi-page

  [Test]
  [Category("Unit")]
  public void BigTiffFile_ImageCount_Returns1ForSinglePage() {
    var btf = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PixelData = new byte[16],
    };
    Assert.That(_ImageCount(btf), Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void BigTiffFile_ImageCount_Returns2ForOneAdditionalPage() {
    var btf = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PixelData = new byte[16],
      Pages = [
        new BigTiffPage { Width = 8, Height = 8, SamplesPerPixel = 1, BitsPerSample = 8, PixelData = new byte[64] },
      ],
    };
    Assert.That(_ImageCount(btf), Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void BigTiffFile_ImageCount_Returns3ForTwoAdditionalPages() {
    var btf = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PixelData = new byte[16],
      Pages = [
        new BigTiffPage { Width = 8, Height = 8, SamplesPerPixel = 1, BitsPerSample = 8, PixelData = new byte[64] },
        new BigTiffPage { Width = 2, Height = 2, SamplesPerPixel = 3, BitsPerSample = 8, PixelData = new byte[12], PhotometricInterpretation = 2 },
      ],
    };
    Assert.That(_ImageCount(btf), Is.EqualTo(3));
  }

  [Test]
  [Category("Integration")]
  public void BigTiffFile_ToRawImage_Index0_ReturnsFirstIfd() {
    var btf = new BigTiffFile {
      Width = 4, Height = 3, SamplesPerPixel = 1, BitsPerSample = 8,
      PixelData = new byte[12],
      Pages = [
        new BigTiffPage { Width = 8, Height = 6, SamplesPerPixel = 3, BitsPerSample = 8, PixelData = new byte[144], PhotometricInterpretation = 2 },
      ],
    };
    var raw = _ToRawImage(btf, 0);
    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(4));
      Assert.That(raw.Height, Is.EqualTo(3));
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
    });
  }

  [Test]
  [Category("Integration")]
  public void BigTiffFile_ToRawImage_Index1_ReturnsSecondIfd() {
    var btf = new BigTiffFile {
      Width = 4, Height = 3, SamplesPerPixel = 1, BitsPerSample = 8,
      PixelData = new byte[12],
      Pages = [
        new BigTiffPage { Width = 8, Height = 6, SamplesPerPixel = 3, BitsPerSample = 8, PixelData = new byte[144], PhotometricInterpretation = 2 },
      ],
    };
    var raw = _ToRawImage(btf, 1);
    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(8));
      Assert.That(raw.Height, Is.EqualTo(6));
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    });
  }

  [Test]
  [Category("Unit")]
  public void BigTiffFile_ToRawImage_IndexEqualToTotal_Throws() {
    var btf = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PixelData = new byte[16],
    };
    Assert.Throws<ArgumentOutOfRangeException>(() => _ToRawImage(btf, 1));
  }

  [Test]
  [Category("Integration")]
  public void BigTiffFile_ToRawImages_ReturnsTwoItems() {
    var btf = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PixelData = new byte[16],
      Pages = [
        new BigTiffPage { Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8, PixelData = new byte[4] },
      ],
    };
    var images = _ToRawImages(btf);
    Assert.That(images, Has.Count.EqualTo(2));
  }

  [Test]
  [Category("Integration")]
  public void BigTiffFile_ToRawImage_Index0_PixelDataPreserved() {
    var pixels = new byte[] { 10, 20, 30, 40 };
    var btf = new BigTiffFile {
      Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8,
      PixelData = pixels,
      Pages = [
        new BigTiffPage { Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8, PixelData = new byte[4] },
      ],
    };
    var raw = _ToRawImage(btf, 0);
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  #endregion

  #region ApngFile multi-image

  [Test]
  [Category("Unit")]
  public void ApngFile_ImageCount_ReturnsFrameCount() {
    var frame0 = new ApngFrame { Width = 4, Height = 4, PixelData = _SplitIntoRows(new byte[48], 12, 4) };
    var frame1 = new ApngFrame { Width = 4, Height = 4, PixelData = _SplitIntoRows(new byte[48], 12, 4) };
    var apng = new ApngFile { Width = 4, Height = 4, BitDepth = 8, ColorType = PngColorType.RGB, Frames = [frame0, frame1] };
    Assert.That(_ImageCount(apng), Is.EqualTo(2));
  }

  [Test]
  [Category("Integration")]
  public void ApngFile_ToRawImage_Index0_ReturnsValidDimensions() {
    var frame0 = new ApngFrame { Width = 4, Height = 4, PixelData = _SplitIntoRows(new byte[48], 12, 4) };
    var apng = new ApngFile { Width = 4, Height = 4, BitDepth = 8, ColorType = PngColorType.RGB, Frames = [frame0] };
    var raw = _ToRawImage(apng, 0);
    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(4));
      Assert.That(raw.Height, Is.EqualTo(4));
    });
  }

  [Test]
  [Category("Unit")]
  public void ApngFile_ToRawImage_OutOfRange_Throws() {
    var apng = new ApngFile { Width = 4, Height = 4, BitDepth = 8, ColorType = PngColorType.RGB, Frames = [] };
    Assert.Throws<ArgumentOutOfRangeException>(() => _ToRawImage(apng, 0));
  }

  [Test]
  [Category("Unit")]
  public void ApngFile_Capabilities_IncludesMultiImage() {
    var caps = _GetCapabilities<ApngFile>();
    Assert.That(caps.HasFlag(FormatCapability.MultiImage), Is.True);
  }

  #endregion

  #region MngFile multi-image

  [Test]
  [Category("Unit")]
  public void MngFile_ImageCount_ReturnsFrameCount() {
    var png0 = _CreateSmallPngBytes(4, 4);
    var png1 = _CreateSmallPngBytes(4, 4);
    var mng = new MngFile { Width = 4, Height = 4, Frames = [png0, png1] };
    Assert.That(_ImageCount(mng), Is.EqualTo(2));
  }

  [Test]
  [Category("Integration")]
  public void MngFile_ToRawImage_Index0_ReturnsValidDimensions() {
    var png0 = _CreateSmallPngBytes(4, 4);
    var mng = new MngFile { Width = 4, Height = 4, Frames = [png0] };
    var raw = _ToRawImage(mng, 0);
    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(4));
      Assert.That(raw.Height, Is.EqualTo(4));
    });
  }

  [Test]
  [Category("Integration")]
  public void MngFile_ToRawImage_Index1_ReturnsSecondFrame() {
    var png0 = _CreateSmallPngBytes(4, 4);
    var png1 = _CreateSmallPngBytes(8, 8);
    var mng = new MngFile { Width = 8, Height = 8, Frames = [png0, png1] };
    var raw = _ToRawImage(mng, 1);
    Assert.That(raw.Width, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void MngFile_ToRawImage_OutOfRange_Throws() {
    var mng = new MngFile { Width = 4, Height = 4, Frames = [] };
    Assert.Throws<ArgumentOutOfRangeException>(() => _ToRawImage(mng, 0));
  }

  [Test]
  [Category("Unit")]
  public void MngFile_Capabilities_IncludesMultiImage() {
    var caps = _GetCapabilities<MngFile>();
    Assert.That(caps.HasFlag(FormatCapability.MultiImage), Is.True);
  }

  #endregion

  #region FliFile multi-image

  [Test]
  [Category("Unit")]
  public void FliFile_ImageCount_ReturnsFrameCount() {
    var frame = new FliFrame { Chunks = [new FliFrameChunk { ChunkType = FliChunkType.Black, Data = [] }] };
    var fli = new FliFile { Width = 8, Height = 8, FrameCount = 2, Frames = [frame, frame] };
    Assert.That(_ImageCount(fli), Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FliFile_ImageCount_ReturnsZeroForEmpty() {
    var fli = new FliFile { Width = 8, Height = 8, FrameCount = 0, Frames = [] };
    Assert.That(_ImageCount(fli), Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void FliFile_ToRawImage_Index0_ReturnsCanvasDimensions() {
    var frame = new FliFrame { Chunks = [new FliFrameChunk { ChunkType = FliChunkType.Black, Data = [] }] };
    var fli = new FliFile { Width = 16, Height = 12, FrameCount = 1, Frames = [frame] };
    var raw = _ToRawImage(fli, 0);
    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(16));
      Assert.That(raw.Height, Is.EqualTo(12));
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
    });
  }

  [Test]
  [Category("Unit")]
  public void FliFile_ToRawImage_OutOfRange_Throws() {
    var fli = new FliFile { Width = 8, Height = 8, FrameCount = 0, Frames = [] };
    Assert.Throws<ArgumentOutOfRangeException>(() => _ToRawImage(fli, 0));
  }

  [Test]
  [Category("Unit")]
  public void FliFile_Capabilities_IncludesMultiImage() {
    var caps = _GetCapabilities<FliFile>();
    Assert.That(caps.HasFlag(FormatCapability.MultiImage), Is.True);
  }

  #endregion

  #region IcnsFile multi-image

  [Test]
  [Category("Unit")]
  public void IcnsFile_ImageCount_ReturnsDecodableEntryCount() {
    var pngData = _CreateSmallPngBytes(128, 128);
    var icns = new IcnsFile {
      Entries = [
        new IcnsEntry("ic07", pngData, 128, 128),
        new IcnsEntry("ic08", _CreateSmallPngBytes(256, 256), 256, 256),
      ],
    };
    Assert.That(_ImageCount(icns), Is.EqualTo(2));
  }

  [Test]
  [Category("Integration")]
  public void IcnsFile_ToRawImage_Index0_ReturnsValidImage() {
    var pngData = _CreateSmallPngBytes(128, 128);
    var icns = new IcnsFile {
      Entries = [new IcnsEntry("ic07", pngData, 128, 128)],
    };
    var raw = _ToRawImage(icns, 0);
    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(128));
      Assert.That(raw.Height, Is.EqualTo(128));
    });
  }

  [Test]
  [Category("Unit")]
  public void IcnsFile_ToRawImage_OutOfRange_Throws() {
    var icns = new IcnsFile { Entries = [] };
    Assert.Throws<ArgumentOutOfRangeException>(() => _ToRawImage(icns, 0));
  }

  [Test]
  [Category("Unit")]
  public void IcnsFile_Capabilities_IncludesMultiImage() {
    var caps = _GetCapabilities<IcnsFile>();
    Assert.That(caps.HasFlag(FormatCapability.MultiImage), Is.True);
  }

  #endregion

  #region Null argument validation

  [Test]
  [Category("Unit")]
  public void IcoFile_ImageCount_NullFile_Throws()
    => Assert.Throws<NullReferenceException>(() => IcoFile.ImageCount(null!));

  [Test]
  [Category("Unit")]
  public void IcoFile_ToRawImage_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => IcoFile.ToRawImage(null!, 0));

  [Test]
  [Category("Unit")]
  public void DcxFile_ImageCount_NullFile_Throws()
    => Assert.Throws<NullReferenceException>(() => DcxFile.ImageCount(null!));

  [Test]
  [Category("Unit")]
  public void DcxFile_ToRawImage_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => DcxFile.ToRawImage(null!, 0));

  [Test]
  [Category("Unit")]
  public void MpoFile_ImageCount_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => MpoFile.ImageCount(null!));

  [Test]
  [Category("Unit")]
  public void MpoFile_ToRawImage_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => MpoFile.ToRawImage(null!, 0));

  [Test]
  [Category("Unit")]
  public void TiffFile_ImageCount_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => TiffFile.ImageCount(null!));

  [Test]
  [Category("Unit")]
  public void TiffFile_ToRawImage_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => TiffFile.ToRawImage(null!, 0));

  [Test]
  [Category("Unit")]
  public void BigTiffFile_ImageCount_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => BigTiffFile.ImageCount(null!));

  [Test]
  [Category("Unit")]
  public void BigTiffFile_ToRawImage_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => BigTiffFile.ToRawImage(null!, 0));

  [Test]
  [Category("Unit")]
  public void AniFile_ToRawImage_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => AniFile.ToRawImage(null!, 0));

  [Test]
  [Category("Unit")]
  public void ApngFile_ToRawImage_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => ApngFile.ToRawImage(null!, 0));

  [Test]
  [Category("Unit")]
  public void MngFile_ToRawImage_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => MngFile.ToRawImage(null!, 0));

  [Test]
  [Category("Unit")]
  public void FliFile_ToRawImage_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => FliFile.ToRawImage(null!, 0));

  [Test]
  [Category("Unit")]
  public void IcnsFile_ImageCount_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => IcnsFile.ImageCount(null!));

  [Test]
  [Category("Unit")]
  public void IcnsFile_ToRawImage_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => IcnsFile.ToRawImage(null!, 0));

  [Test]
  [Category("Unit")]
  public void CurFile_ToRawImage_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => CurFile.ToRawImage(null!, 0));

  #endregion

  #region ToRawImages default implementation

  [Test]
  [Category("Integration")]
  public void ToRawImages_DefaultImplementation_MatchesIndexedAccess() {
    var ico = _CreateIcoFileWithPngEntries(3, 16);
    var images = _ToRawImages(ico);
    Assert.That(images, Has.Count.EqualTo(3));
    for (var i = 0; i < images.Count; ++i) {
      var indexed = _ToRawImage(ico, i);
      Assert.Multiple(() => {
        Assert.That(images[i].Width, Is.EqualTo(indexed.Width));
        Assert.That(images[i].Height, Is.EqualTo(indexed.Height));
        Assert.That(images[i].Format, Is.EqualTo(indexed.Format));
      });
    }
  }

  [Test]
  [Category("Integration")]
  public void ToRawImages_EmptyFile_ReturnsEmptyList() {
    var dcx = new DcxFile { Pages = [] };
    var images = _ToRawImages(dcx);
    Assert.That(images, Is.Empty);
  }

  [Test]
  [Category("Integration")]
  public void ToRawImages_SingleItem_ReturnsSingleElementList() {
    var dcx = new DcxFile { Pages = [_CreateSmallPcxFile(8, 8)] };
    var images = _ToRawImages(dcx);
    Assert.That(images, Has.Count.EqualTo(1));
  }

  #endregion

  #region Cross-format: multi-image pixel data isolation

  [Test]
  [Category("Integration")]
  public void TiffFile_MultiPage_EachPageHasIndependentPixelData() {
    var page0Pixels = new byte[] { 10, 20, 30, 40 };
    var page1Pixels = new byte[] { 50, 60, 70, 80, 90, 100, 110, 120 };
    var tiff = new TiffFile {
      Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8,
      PixelData = page0Pixels, ColorMode = TiffColorMode.Grayscale,
      Pages = [
        new TiffPage { Width = 2, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8, PixelData = page1Pixels, ColorMode = TiffColorMode.Grayscale },
      ],
    };
    var raw0 = _ToRawImage(tiff, 0);
    var raw1 = _ToRawImage(tiff, 1);
    Assert.Multiple(() => {
      Assert.That(raw0.PixelData, Is.EqualTo(page0Pixels));
      Assert.That(raw1.PixelData, Is.EqualTo(page1Pixels));
      Assert.That(raw0.PixelData, Is.Not.EqualTo(raw1.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void BigTiffFile_MultiPage_EachPageHasIndependentPixelData() {
    var page0Pixels = new byte[] { 10, 20, 30, 40 };
    var page1Pixels = new byte[] { 50, 60, 70, 80, 90, 100, 110, 120 };
    var btf = new BigTiffFile {
      Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8,
      PixelData = page0Pixels,
      Pages = [
        new BigTiffPage { Width = 2, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8, PixelData = page1Pixels },
      ],
    };
    var raw0 = _ToRawImage(btf, 0);
    var raw1 = _ToRawImage(btf, 1);
    Assert.Multiple(() => {
      Assert.That(raw0.PixelData, Is.EqualTo(page0Pixels));
      Assert.That(raw1.PixelData, Is.EqualTo(page1Pixels));
    });
  }

  [Test]
  [Category("Integration")]
  public void DcxFile_MultiPage_DifferentDimensionsPerPage() {
    var page0 = _CreateSmallPcxFile(4, 2);
    var page1 = _CreateSmallPcxFile(32, 24);
    var dcx = new DcxFile { Pages = [page0, page1] };
    var raw0 = _ToRawImage(dcx, 0);
    var raw1 = _ToRawImage(dcx, 1);
    Assert.Multiple(() => {
      Assert.That(raw0.Width, Is.EqualTo(4));
      Assert.That(raw0.Height, Is.EqualTo(2));
      Assert.That(raw1.Width, Is.EqualTo(32));
      Assert.That(raw1.Height, Is.EqualTo(24));
    });
  }

  #endregion

  #region Helper for capabilities

  private static FormatCapability _GetCapabilities<T>() where T : IImageFormatMetadata<T>
    => T.Capabilities;

  #endregion
}
