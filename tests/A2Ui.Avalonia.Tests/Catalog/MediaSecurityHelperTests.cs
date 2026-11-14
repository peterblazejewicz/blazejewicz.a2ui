using System.Net;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Blazejewicz.A2Ui.Avalonia.Catalog;
using Blazejewicz.A2Ui.Avalonia.Catalog.Entries;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Surfaces;
using Blazejewicz.A2Ui.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    [Fact]
    public void IsSafeMediaUrl_CustomAllowedSchemes_RespectsConfiguredSet()
    {
        var opts = new A2UiAvaloniaOptions();
        Assert.False(
            MediaSecurityHelper.IsSafeMediaUrl(
                "avares://Blazejewicz.A2Ui.Avalonia/Assets/logo.png",
                opts.AllowedMediaSchemes
            )
        );

        opts.AllowedMediaSchemes.Add("avares");
        Assert.True(
            MediaSecurityHelper.IsSafeMediaUrl(
                "avares://Blazejewicz.A2Ui.Avalonia/Assets/logo.png",
                opts.AllowedMediaSchemes
            )
        );
        Assert.Equal(
            "avares://Blazejewicz.A2Ui.Avalonia/Assets/logo.png",
            MediaSecurityHelper.FormatSafeUrl(
                "avares://Blazejewicz.A2Ui.Avalonia/Assets/logo.png",
                opts.AllowedMediaSchemes
            )
        );
    }

    [Fact]
    public void AddA2UiAvalonia_WithOptionsCallback_ConfiguresCatalogEntries()
    {
        var services = new ServiceCollection();
        services.AddA2UiAvalonia(opts =>
        {
            opts.AllowedMediaSchemes.Add("avares");
            opts.MaxImageBytes = 512 * 1024;
        });

        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<CatalogRegistry>();
        Assert.True(catalog.TryGetEntry("Video", out var videoEntry));

        var dm = new DataModel();
        var ctx = new MockRenderContext(dm);
        var comp = new VideoComponent
        {
            Id = "v-avares",
            Url = DynamicValue.FromString("avares://Blazejewicz.A2Ui.Avalonia/Assets/intro.mp4"),
        };

        var tb = Assert.IsType<TextBlock>(videoEntry.Create(comp, dm, ctx));
        Assert.Contains("avares://Blazejewicz.A2Ui.Avalonia/Assets/intro.mp4", tb.Text);
    }

    [AvaloniaFact]
    public async Task ImageCatalogEntry_OversizedContentLength_RejectsBeforeReadingBodyAsync()
    {
        using var logProvider = new TestLoggerProvider();
        using var loggerFactory = logProvider.CreateFactory();
        bool streamReadAttempted = false;

        using var http = new HttpClient(
            new StubHttpMessageHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new TrackingStreamContent(
                        totalBytes: 2048,
                        reportContentLength: true,
                        onRead: () => streamReadAttempted = true
                    ),
                };
                return Task.FromResult(response);
            })
        );

        var opts = new A2UiAvaloniaOptions { MaxImageBytes = 1024 };
        var entry = new ImageCatalogEntry(loggerFactory.CreateLogger<ImageCatalogEntry>(), http, opts);
        var img = new Image { Tag = "https://example.com/huge.png" };

        await entry.LoadImageAsync(img, "https://example.com/huge.png", CancellationToken.None).ConfigureAwait(true);

        Assert.False(streamReadAttempted);
        Assert.Null(img.Source);
        Assert.Contains(logProvider.Entries, e => e.EventId.Id == 1 && e.Level == LogLevel.Warning);
    }

    [AvaloniaFact]
    public async Task ImageCatalogEntry_OversizedChunkedStream_AbortsImmediatelyWhenLimitExceededAsync()
    {
        using var logProvider = new TestLoggerProvider();
        using var loggerFactory = logProvider.CreateFactory();
        int bytesProduced = 0;

        using var http = new HttpClient(
            new StubHttpMessageHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new TrackingStreamContent(
                        totalBytes: 10 * 1024 * 1024,
                        reportContentLength: false,
                        onChunkProduced: count => bytesProduced += count
                    ),
                };
                return Task.FromResult(response);
            })
        );

        var opts = new A2UiAvaloniaOptions { MaxImageBytes = 4096 };
        var entry = new ImageCatalogEntry(loggerFactory.CreateLogger<ImageCatalogEntry>(), http, opts);
        var img = new Image { Tag = "https://example.com/chunked.png" };

        await entry.LoadImageAsync(img, "https://example.com/chunked.png", CancellationToken.None).ConfigureAwait(true);

        Assert.Null(img.Source);
        Assert.True(bytesProduced < 200_000, $"Expected stream reading to abort early, but read {bytesProduced} bytes.");
        Assert.Contains(logProvider.Entries, e => e.EventId.Id == 1 && e.Level == LogLevel.Warning);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler
    ) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => handler(request);
    }

    private sealed class TrackingStreamContent : HttpContent
    {
        private readonly int _totalBytes;
        private readonly bool _reportContentLength;
        private readonly Action? _onRead;
        private readonly Action<int>? _onChunkProduced;

        public TrackingStreamContent(
            int totalBytes,
            bool reportContentLength,
            Action? onRead = null,
            Action<int>? onChunkProduced = null
        )
        {
            this._totalBytes = totalBytes;
            this._reportContentLength = reportContentLength;
            this._onRead = onRead;
            this._onChunkProduced = onChunkProduced;
            if (reportContentLength)
            {
                this.Headers.ContentLength = totalBytes;
            }
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.CompletedTask;

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new BoundedProducerStream(this._totalBytes, this._onRead, this._onChunkProduced));

        protected override bool TryComputeLength(out long length)
        {
            if (this._reportContentLength)
            {
                length = this._totalBytes;
                return true;
            }

            length = 0;
            return false;
        }
    }

    private sealed class BoundedProducerStream(
        int totalBytes,
        Action? onRead,
        Action<int>? onChunkProduced
    ) : Stream
    {
        private int _remaining = totalBytes;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            onRead?.Invoke();
            if (this._remaining <= 0)
            {
                return 0;
            }

            int toCopy = Math.Min(Math.Min(count, 2048), this._remaining);
            this._remaining -= toCopy;
            onChunkProduced?.Invoke(toCopy);
            return toCopy;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
