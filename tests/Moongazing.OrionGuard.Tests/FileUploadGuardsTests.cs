using System.Text;
using Moongazing.OrionGuard.Extensions;

namespace Moongazing.OrionGuard.Tests;

public class FileUploadGuardsTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private const string SvgWithOnload = "<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(document.domain)\"></svg>";

    private static byte[] PngWithPayloadAt(int offset, string payload)
    {
        var content = new byte[offset + payload.Length];
        PngSignature.CopyTo(content, 0);
        Encoding.ASCII.GetBytes(payload).CopyTo(content, offset);
        return content;
    }

    #region AgainstDangerousFileExtension

    [Theory]
    [InlineData("shell.aspx")]
    [InlineData("handler.ashx")]
    [InlineData("service.asmx")]
    [InlineData("shell.php")]
    [InlineData("shell.phtml")]
    [InlineData("shell.jsp")]
    [InlineData("web.config")]
    [InlineData("page.html")]
    [InlineData("page.htm")]
    [InlineData("image.svg")]
    [InlineData("shortcut.lnk")]
    [InlineData("app.jar")]
    [InlineData("evil.exe.")]
    [InlineData("evil.exe ")]
    [InlineData("evil.exe. . ")]
    [InlineData("evil.exe::$DATA")]
    [InlineData("SHELL.ASPX")]
    public void AgainstDangerousFileExtension_ShouldThrow_WhenExtensionIsDangerousAfterNormalization(string fileName)
    {
        Assert.Throws<ArgumentException>(() => fileName.AgainstDangerousFileExtension(nameof(fileName)));
    }

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("report.final.pdf")]
    [InlineData("uploads/2024/archive.zip")]
    [InlineData("C:\\uploads\\photo.png")]
    [InlineData("notes")]
    public void AgainstDangerousFileExtension_ShouldNotThrow_WhenExtensionIsOrdinary(string fileName)
    {
        var exception = Record.Exception(() => fileName.AgainstDangerousFileExtension(nameof(fileName)));
        Assert.Null(exception);
    }

    #endregion

    #region AgainstDisallowedExtension

    [Theory]
    [InlineData("evil.exe:.jpg")]
    [InlineData("evil.exe:x.jpg")]
    public void AgainstDisallowedExtension_ShouldThrow_WhenNameAddressesAlternateDataStream(string fileName)
    {
        Assert.Throws<ArgumentException>(() => fileName.AgainstDisallowedExtension([".jpg"], nameof(fileName)));
    }

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("PHOTO.JPG")]
    public void AgainstDisallowedExtension_ShouldNotThrow_WhenExtensionIsAllowed(string fileName)
    {
        var exception = Record.Exception(() => fileName.AgainstDisallowedExtension([".jpg", "png"], nameof(fileName)));
        Assert.Null(exception);
    }

    #endregion

    #region AgainstMaliciousContent

    [Fact]
    public void AgainstMaliciousContent_ShouldThrow_WhenSvgCarriesScript()
    {
        var svg = Encoding.UTF8.GetBytes(SvgWithOnload);

        // The signature check alone accepts this file: it really is an SVG.
        Assert.Null(Record.Exception(() => svg.AgainstFakeMimeType(".svg", "file")));
        Assert.Throws<ArgumentException>(() => svg.AgainstMaliciousContent(".svg", "file"));
    }

    [Theory]
    [InlineData("<?php system($_GET['c']); ?>")]
    [InlineData("<SCRIPT>alert(1)</SCRIPT>")]
    [InlineData("JavaScript:alert(1)")]
    public void AgainstMaliciousContent_ShouldThrow_WhenMarkerLiesPastTheFirst8KB(string payload)
    {
        var png = PngWithPayloadAt(8200, payload);

        Assert.Throws<ArgumentException>(() => png.AgainstMaliciousContent(".png", "file"));
    }

    [Fact]
    public void AgainstMaliciousContent_ShouldNotThrow_WhenImageIsOrdinary()
    {
        var png = new byte[64 * 1024];
        PngSignature.CopyTo(png, 0);
        for (int i = PngSignature.Length; i < png.Length; i++)
            png[i] = (byte)(i * 31 % 251);

        var exception = Record.Exception(() => png.AgainstMaliciousContent(".png", "file"));
        Assert.Null(exception);
    }

    [Fact]
    public void AgainstMaliciousContent_ShouldNotThrow_WhenTypeIsNotInspected()
    {
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7 <script>");

        var exception = Record.Exception(() => pdf.AgainstMaliciousContent(".pdf", "file"));
        Assert.Null(exception);
    }

    #endregion

    #region AgainstMaliciousContent (stream)

    [Fact]
    public void AgainstMaliciousContentStream_ShouldThrow_WhenMarkerLiesPastTheFirst8KB()
    {
        using var stream = new MemoryStream(PngWithPayloadAt(8200, "<?php"));

        Assert.Throws<ArgumentException>(() => stream.AgainstMaliciousContent(".png", "file", 1024 * 1024));
    }

    [Fact]
    public void AgainstMaliciousContentStream_ShouldThrow_WhenMarkerLiesBeforeTheCurrentPosition()
    {
        // A header sniffer already read past the payload; the scan must still start at byte zero.
        using var stream = new MemoryStream(PngWithPayloadAt(100, "<?php"));
        stream.Position = 5000;

        Assert.Throws<ArgumentException>(() => stream.AgainstMaliciousContent(".png", "file", 1024 * 1024));
        Assert.Equal(5000, stream.Position);
    }

    [Fact]
    public void AgainstMaliciousContentStream_ShouldThrow_WhenStreamExceedsScanLimit()
    {
        using var stream = new MemoryStream(new byte[2048]);

        Assert.Throws<ArgumentException>(() => stream.AgainstMaliciousContent(".png", "file", 1024));
    }

    [Fact]
    public void AgainstMaliciousContentStream_ShouldThrow_WhenSvg()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(SvgWithOnload));

        Assert.Throws<ArgumentException>(() => stream.AgainstMaliciousContent(".svg", "file", 1024));
    }

    [Fact]
    public void AgainstMaliciousContentStream_ShouldRestorePosition_WhenImageIsOrdinary()
    {
        var png = new byte[20_000];
        PngSignature.CopyTo(png, 0);
        using var stream = new MemoryStream(png);
        stream.Position = 3;

        stream.AgainstMaliciousContent(".png", "file", png.Length);

        Assert.Equal(3, stream.Position);
    }

    #endregion
}
