using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Blazejewicz.A2Ui.Avalonia.Catalog;
using Blazejewicz.A2Ui.Avalonia.Catalog.Entries;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Surfaces;
using Xunit;

namespace Blazejewicz.A2Ui.Avalonia.Tests.Catalog;

public sealed class MediaSecurityHelperTests
{
    [Theory]
    [InlineData("https://example.com/image.png")]
    [InlineData("http://example.com/video.mp4")]
    [InlineData(
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="
    )]
    [InlineData("/assets/logo.png")]
    [InlineData("assets/logo.png")]
    [InlineData("relative/path/to/media.mp3")]
    public void IsSafeMediaUrl_AllowedUrls_ReturnsTrue(string url)
    {
        Assert.True(MediaSecurityHelper.IsSafeMediaUrl(url));
        Assert.True(MediaSecurityHelper.IsAllowedMediaUrl(url));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/sensitive/file.png")]
    [InlineData("vbscript:MsgBox(\"xss\")")]
    [InlineData("blob:https://example.com/d39a6742-b36e")]
    [InlineData("content://media/external/images/media/1")]
    [InlineData("//evil.com/x.png")]
    [InlineData(@"\\evil.com\x.png")]
    [InlineData(@"\foo\bar.png")]
    [InlineData("/C:/sensitive/file.png")]
    [InlineData("C:/sensitive/file.png")]
    [InlineData(@"C:\sensitive\file.png")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("http://example.com/\0evil")]
    public void IsSafeMediaUrl_DisallowedOrInvalidUrls_ReturnsFalse(string? url)
    {
        Assert.False(MediaSecurityHelper.IsSafeMediaUrl(url));
        Assert.False(MediaSecurityHelper.IsAllowedMediaUrl(url));
    }

    [Fact]
    public void FormatSafeUrl_ValidAndInvalidUrls_BehavesCorrectly()
    {
        Assert.Equal("https://example.com/video.mp4", MediaSecurityHelper.FormatSafeUrl("https://example.com/video.mp4"));
        Assert.Equal("no url", MediaSecurityHelper.FormatSafeUrl(null));
        Assert.Equal("blocked unsafe url", MediaSecurityHelper.FormatSafeUrl("   "));
        Assert.Equal("blocked unsafe url", MediaSecurityHelper.FormatSafeUrl("javascript:alert(1)"));
        Assert.Equal("blocked unsafe url", MediaSecurityHelper.FormatSafeUrl(@"\unc\path.mp4"));
        Assert.Equal("blocked unsafe url", MediaSecurityHelper.FormatSafeUrl("/C:/windows/file.mp4"));
    }

    [AvaloniaFact]
    public void ImageCatalogEntry_UnsafeUrl_DoesNotThrowAndDoesNotLoad()
    {
        var dm = new DataModel();
        var ctx = new MockRenderContext(dm);
        var entry = new ImageCatalogEntry();

        var comp = new ImageComponent { Id = "img1", Url = DynamicValue.FromString("javascript:alert(1)") };

        var control = entry.Create(comp, dm, ctx);
        var img = Assert.IsType<Image>(control);
        Assert.Null(img.Tag);
        Assert.Null(img.Source);

        // Update with another unsafe URL
        var updatedComp = new ImageComponent { Id = "img1", Url = DynamicValue.FromString("file:///C:/passwords.txt") };

        bool updated = entry.Update(img, updatedComp, dm, ctx);
        Assert.True(updated);
        Assert.Null(img.Tag);
        Assert.Null(img.Source);
    }

    [AvaloniaFact]
    public void VideoCatalogEntry_UnsafeUrl_DoesNotThrowAndRendersBlockedPlaceholder()
    {
        var dm = new DataModel();
        var ctx = new MockRenderContext(dm);
        var entry = new VideoCatalogEntry();

        var comp = new VideoComponent { Id = "v1", Url = DynamicValue.FromString("javascript:alert(1)") };

        var control = entry.Create(comp, dm, ctx);
        var tb = Assert.IsType<TextBlock>(control);
        Assert.Contains("[Video:", tb.Text);
        Assert.Contains("blocked unsafe url", tb.Text);

        // Update with another unsafe URL
        var updatedComp = new VideoComponent { Id = "v1", Url = DynamicValue.FromString("//evil.com/video.mp4") };

        bool updated = entry.Update(tb, updatedComp, dm, ctx);
        Assert.True(updated);
        Assert.Contains("blocked unsafe url", tb.Text);
    }

    [AvaloniaFact]
    public void AudioPlayerCatalogEntry_UnsafeUrl_DoesNotThrowAndRendersBlockedPlaceholder()
    {
        var dm = new DataModel();
        var ctx = new MockRenderContext(dm);
        var entry = new AudioPlayerCatalogEntry();

        var comp = new AudioPlayerComponent
        {
            Id = "a1",
            Url = DynamicValue.FromString("blob:http://example.com/audio"),
        };

        var control = entry.Create(comp, dm, ctx);
        var tb = Assert.IsType<TextBlock>(control);
        Assert.Contains("[AudioPlayer:", tb.Text);
        Assert.Contains("blocked unsafe url", tb.Text);

        // Update with another unsafe URL
        var updatedComp = new AudioPlayerComponent { Id = "a1", Url = DynamicValue.FromString("file:///etc/passwd") };

        bool updated = entry.Update(tb, updatedComp, dm, ctx);
        Assert.True(updated);
        Assert.Contains("blocked unsafe url", tb.Text);
    }
}
