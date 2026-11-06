using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using WMCL.Download;

namespace WMCL.Download.Tests;

[Trait("Kind", "Unit")]
public sealed class DownloadManagerIntegrityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-integrity-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _data = "abcdef"u8.ToArray();
    private string Destination => Path.Combine(_root, "file.bin");
    private string Part => Destination + ".part";
    private string Sha1 => Convert.ToHexStringLower(SHA1.HashData(_data));

    public DownloadManagerIntegrityTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectsWrongSizeWithoutHashAndPreservesDestination(bool contentLength)
    {
        await File.WriteAllTextAsync(Destination, "original");
        using var http = Client(_ =>
        {
            HttpContent content = contentLength
                ? new ByteArrayContent("abc"u8.ToArray())
                : new StreamContent(new InterruptedStream("abc"u8.ToArray()));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        await Assert.ThrowsAsync<DownloadException>(() => Download(http, size: 100));

        Assert.Equal("original", await File.ReadAllTextAsync(Destination));
        Assert.Empty(Directory.GetFiles(_root, "*.part"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("items 3-5/6")]
    [InlineData("bytes 2-5/6")]
    [InlineData("bytes 3-4/6")]
    [InlineData("bytes 3-5/7")]
    [InlineData("bytes 3-5/*")]
    [InlineData("bytes */6")]
    public async Task RejectsMissingOrInconsistentRange(string? range)
    {
        await File.WriteAllBytesAsync(Part, _data[..3]);
        await File.WriteAllTextAsync(Destination, "original");
        using var http = Client(request =>
        {
            Assert.Equal(3, request.Headers.Range!.Ranges.Single().From);
            var response = Response(HttpStatusCode.PartialContent, _data[3..]);
            if (range is not null) response.Content.Headers.TryAddWithoutValidation("Content-Range", range);
            return response;
        });

        await Assert.ThrowsAsync<DownloadException>(() => Download(http, Sha1, _data.Length));

        Assert.Equal("original", await File.ReadAllTextAsync(Destination));
        Assert.False(File.Exists(Part));
    }

    [Fact]
    public async Task ResumesVerifiedHashWithConsistentRange()
    {
        await File.WriteAllBytesAsync(Part, _data[..3]);
        using var http = Client(request =>
        {
            Assert.Equal(3, request.Headers.Range!.Ranges.Single().From);
            return Partial(_data[3..]);
        });

        await Download(http, Sha1, _data.Length);

        Assert.Equal(_data, await File.ReadAllBytesAsync(Destination));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task RejectsRangeBodyLengthMismatch(int bodyLength)
    {
        await File.WriteAllBytesAsync(Part, _data[..3]);
        using var http = Client(_ => Partial(new byte[bodyLength]));

        await Assert.ThrowsAsync<DownloadException>(() => Download(http, Sha1, _data.Length));

        Assert.Empty(Directory.GetFiles(_root));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task RejectsActualRangeBodyLengthMismatchWithoutContentLength(int bodyLength)
    {
        await File.WriteAllBytesAsync(Part, _data[..3]);
        using var http = Client(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new StreamContent(new InterruptedStream(new byte[bodyLength]))
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(3, 5, 6);
            return response;
        });

        await Assert.ThrowsAsync<DownloadException>(() => Download(http, Sha1, _data.Length));

        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task FullResponseToRangeSafelyOverwritesPart()
    {
        await File.WriteAllBytesAsync(Part, "bad"u8.ToArray());
        using var http = Client(request =>
        {
            Assert.NotNull(request.Headers.Range);
            return Response(HttpStatusCode.OK, _data);
        });

        await Download(http, Sha1, _data.Length);

        Assert.Equal(_data, await File.ReadAllBytesAsync(Destination));
        Assert.False(File.Exists(Part));
    }

    [Fact]
    public async Task HashFailureCleansPartAndProtectsDestination()
    {
        await File.WriteAllTextAsync(Destination, "original");
        using var http = Client(_ => Response(HttpStatusCode.OK, "wrong!"u8.ToArray()));

        await Assert.ThrowsAsync<DownloadException>(() => Download(http, Sha1, _data.Length));

        Assert.Equal("original", await File.ReadAllTextAsync(Destination));
        Assert.False(File.Exists(Part));
    }

    [Fact]
    public async Task InvalidRangeRetryStartsFresh()
    {
        await File.WriteAllBytesAsync(Part, _data[..3]);
        var requests = 0;
        using var http = Client(request =>
        {
            if (++requests == 1)
            {
                Assert.NotNull(request.Headers.Range);
                return Response(HttpStatusCode.PartialContent, _data[3..]);
            }
            Assert.Null(request.Headers.Range);
            return Response(HttpStatusCode.OK, _data);
        });

        await Download(http, Sha1, _data.Length, retryCount: 1);

        Assert.Equal(2, requests);
        Assert.Equal(_data, await File.ReadAllBytesAsync(Destination));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationKeepsOnlyPartAndResumeUsesHashPolicy(bool hasHash)
    {
        await File.WriteAllTextAsync(Destination, "original");
        using var cancellation = new CancellationTokenSource();
        using (var http = Client(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new InterruptedStream(_data[..3], () =>
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }))
        }))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                Download(http, hasHash ? Sha1 : null, _data.Length, ct: cancellation.Token));
        }

        Assert.Equal(_data[..3], await File.ReadAllBytesAsync(Part));
        Assert.Equal("original", await File.ReadAllTextAsync(Destination));
        Assert.Equal(2, Directory.GetFiles(_root).Length);
        using var resumed = Client(request =>
        {
            if (hasHash)
            {
                Assert.Equal(3, request.Headers.Range!.Ranges.Single().From);
                return Partial(_data[3..]);
            }
            Assert.Null(request.Headers.Range);
            return Response(HttpStatusCode.OK, _data);
        });

        await Download(resumed, hasHash ? Sha1 : null, _data.Length);

        Assert.Equal(_data, await File.ReadAllBytesAsync(Destination));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task LegacyPartWithoutHashIsNeverAppended()
    {
        await File.WriteAllTextAsync(Part, "old");
        using var http = Client(request =>
        {
            Assert.Null(request.Headers.Range);
            return Response(HttpStatusCode.OK, _data);
        });

        await Download(http, size: _data.Length);

        Assert.Equal(_data, await File.ReadAllBytesAsync(Destination));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MirrorFallbackDoesNotCombineDifferentUnverifiedVersions(bool hasHash)
    {
        var requests = 0;
        using var http = Client(request =>
        {
            if (++requests == 1)
            {
                Assert.Equal("first.example", request.RequestUri!.Host);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new InterruptedStream("old"u8.ToArray(),
                        () => throw new IOException("connection lost")))
                };
            }
            Assert.Equal("second.example", request.RequestUri!.Host);
            if (hasHash)
            {
                Assert.NotNull(request.Headers.Range);
                return Partial(_data[3..]);
            }
            Assert.Null(request.Headers.Range);
            return Response(HttpStatusCode.OK, _data);
        });
        var item = new DownloadItem
        {
            Destination = Destination,
            Sha1 = hasHash ? Sha1 : null,
            Size = _data.Length,
            Urls = ["https://first.example/file", "https://second.example/file"]
        };
        var manager = new DownloadManager(http);

        if (hasHash)
        {
            await Assert.ThrowsAsync<DownloadException>(() => manager.DownloadAsync([item], Options()));
            Assert.Empty(Directory.GetFiles(_root));
        }
        else
        {
            await manager.DownloadAsync([item], Options());
            Assert.Equal(_data, await File.ReadAllBytesAsync(Destination));
        }
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task VerifiedHashCanResumeAcrossMirrors()
    {
        var requests = 0;
        using var http = Client(_ => ++requests == 1
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new InterruptedStream(_data[..3],
                    () => throw new IOException("connection lost")))
            }
            : Partial(_data[3..]));
        var item = new DownloadItem
        {
            Destination = Destination,
            Sha1 = Sha1,
            Size = _data.Length,
            Urls = ["https://first.example/file", "https://second.example/file"]
        };

        await new DownloadManager(http).DownloadAsync([item], Options());

        Assert.Equal(_data, await File.ReadAllBytesAsync(Destination));
        Assert.Equal(2, requests);
    }

    [Theory]
    [InlineData(6, true)]
    [InlineData(100, false)]
    public async Task RangeNotSatisfiableRequiresVerifiedHashAndMatchingSize(long size, bool succeeds)
    {
        await File.WriteAllBytesAsync(Part, _data);
        using var http = Client(_ => Response(HttpStatusCode.RequestedRangeNotSatisfiable, []));
        if (succeeds)
        {
            await Download(http, Sha1, size);
            Assert.Equal(_data, await File.ReadAllBytesAsync(Destination));
        }
        else
        {
            await Assert.ThrowsAsync<DownloadException>(() => Download(http, Sha1, size));
            Assert.False(File.Exists(Destination));
        }
        Assert.False(File.Exists(Part));
    }

    [Fact]
    public async Task RangeNotSatisfiableWithoutHashCannotPromoteSameLengthPart()
    {
        await File.WriteAllBytesAsync(Part, _data);
        using var http = Client(request =>
        {
            Assert.Null(request.Headers.Range);
            return Response(HttpStatusCode.RequestedRangeNotSatisfiable, []);
        });

        await Assert.ThrowsAsync<DownloadException>(() => Download(http, size: _data.Length));

        Assert.Empty(Directory.GetFiles(_root));
    }

    private Task<DownloadSummary> Download(HttpClient http, string? sha1 = null, long? size = null,
        int retryCount = 0, CancellationToken ct = default) =>
        new DownloadManager(http).DownloadAsync(
            [DownloadItem.Create("https://example.com/file", Destination, sha1, size)], Options(retryCount), ct: ct);

    private static DownloadOptions Options(int retryCount = 0) => new()
    {
        RetryCount = retryCount,
        RetryDelay = TimeSpan.Zero,
        VerifyExisting = true
    };

    private static HttpResponseMessage Response(HttpStatusCode status, byte[] body) => new(status)
    {
        Content = new ByteArrayContent(body)
    };

    private static HttpResponseMessage Partial(byte[] body)
    {
        var response = Response(HttpStatusCode.PartialContent, body);
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(3, 5, 6);
        return response;
    }

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new StubHandler(respond));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    private sealed class InterruptedStream(byte[] prefix, Action? interrupt = null) : Stream
    {
        private bool _read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_read)
            {
                interrupt?.Invoke();
                return ValueTask.FromResult(0);
            }
            _read = true;
            prefix.AsMemory().CopyTo(buffer);
            return ValueTask.FromResult(prefix.Length);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
