using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Utils;

namespace SoundFlow.Providers;

/// <summary>
///     Provides audio data from an internet source, supporting both direct audio URLs and HLS (m3u(8)) playlists.
/// </summary>
public sealed class NetworkDataProvider : ISoundDataProvider
{
    private readonly string _url;
    private ISoundDecoder? _decoder;
    private readonly HttpClient _httpClient;
    private Stream? _stream;
    private long? _contentLength;

    private readonly Queue<float> _audioBuffer = new();
    private int _samplePosition;
    private bool _isEndOfStream;
    private readonly object _lock = new();

    // For HLS
    private bool _isHlsStream;
    private readonly List<HlsSegment> _hlsSegments = [];
    private int _currentSegmentIndex;
    private DateTime _lastPlaylistRefreshTime;
    private bool _isEndList;
    private double _hlsTotalDuration;
    private CancellationTokenSource? _cancellationTokenSource;
    private double _hlsTargetDuration = 5;

    /// <summary>
    ///     Initializes a new instance of the <see cref="NetworkDataProvider" /> class.
    /// </summary>
    /// <param name="url">The URL of the audio stream.</param>
    public NetworkDataProvider(string url)
    {
        _url = url ?? throw new ArgumentNullException(nameof(url));
        SampleRate = AudioEngine.Instance.SampleRate;
        _httpClient = new HttpClient();
        Initialize();
    }

    /// <inheritdoc />
    public int Position
    {
        get
        {
            lock (_lock)
            {
                return _samplePosition;
            }
        }
    }

    /// <inheritdoc />
    public int Length { get; private set; }

    /// <inheritdoc />
    public bool CanSeek { get; private set; }

    /// <inheritdoc />
    public SampleFormat SampleFormat { get; private set; }

    /// <inheritdoc />
    public int SampleRate { get; }

    /// <inheritdoc />
    public bool IsDisposed { get; private set; }

    /// <inheritdoc />
    public event EventHandler<EventArgs>? EndOfStreamReached;

    /// <inheritdoc />
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;

    /// <inheritdoc />
    public int ReadBytes(Span<float> buffer)
    {
        if (IsDisposed) return 0;

        var samplesRead = 0;

        lock (_lock)
        {
            while (samplesRead < buffer.Length)
            {
                if (_audioBuffer.Count == 0)
                {
                    if (_isEndOfStream)
                    {
                        if (samplesRead == 0)
                            EndOfStreamReached?.Invoke(this, EventArgs.Empty);

                        break;
                    }

                    Monitor.Wait(_lock, TimeSpan.FromMilliseconds(100));
                    continue;
                }

                buffer[samplesRead++] = _audioBuffer.Dequeue();
            }

            _samplePosition += samplesRead;
            PositionChanged?.Invoke(this, new PositionChangedEventArgs(_samplePosition));
            return samplesRead;
        }
    }

    /// <inheritdoc />
    public void Seek(int sampleOffset)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (!CanSeek)
            throw new NotSupportedException("Seeking is not supported for this stream.");

        lock (_lock)
        {
            if (_isHlsStream)
                SeekInHlsStream(sampleOffset);
            else
                SeekInDirectStream(sampleOffset);
        }
    }

    private async void Initialize()
    {
        _isHlsStream = await IsHlsUrlAsync(_url);
        if (_isHlsStream)
            InitializeHlsStream();
        else
            InitializeDirectStream();
    }

    private async Task<bool> IsHlsUrlAsync(string url)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Head, url);
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (response is { IsSuccessStatusCode: true, Content.Headers.ContentType: not null })
            {
                var contentType = response.Content.Headers.ContentType.MediaType!;
                if (contentType.Equals("application/vnd.apple.mpegurl", StringComparison.OrdinalIgnoreCase) ||
                    contentType.Equals("application/x-mpegURL", StringComparison.OrdinalIgnoreCase) ||
                    contentType.Equals("audio/x-mpegURL", StringComparison.OrdinalIgnoreCase) ||
                    contentType.Equals("audio/mpegurl", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (url.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase))
                return true;

            var content = await DownloadPartialContentAsync(url, 1024);
            if (content != null)
            {
                if (content.Contains("#EXTM3U", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            // Ignore exceptions and default to false
        }

        return false;
    }

    private async Task<string?> DownloadPartialContentAsync(string url, int byteCount)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(0, byteCount - 1);
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            var buffer = new byte[byteCount];
            var bytesRead = await stream.ReadAsync(buffer);
            return Encoding.UTF8.GetString(buffer, 0, bytesRead);
        }
        catch
        {
            return null;
        }
    }

    private void InitializeDirectStream()
    {
        Task.Run(async () =>
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, _url);
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                CanSeek = response.Headers.AcceptRanges.Contains("bytes");

                // 2. Conditional Length Retrieval
                // TODO: Find a more accurate way to get the stream length without the decoder or downloading the whole stream or this
                if (response.Content.Headers.ContentLength is > 0)
                {
                    try
                    {
                        // Download a small chunk (e.g., first 256KB) for temporary decoder
                        var partialContentRequest = new HttpRequestMessage(HttpMethod.Get, _url);
                        partialContentRequest.Headers.Range = new RangeHeaderValue(0,
                            Math.Min(response.Content.Headers.ContentLength.Value, 256 * 1024) -
                            1); // Request up to 256KB
                        var partialContentResponse = await _httpClient.SendAsync(partialContentRequest,
                            HttpCompletionOption.ResponseContentRead);
                        partialContentResponse.EnsureSuccessStatusCode();

                        await using var partialContentStream = await partialContentResponse.Content.ReadAsStreamAsync();
                        var buffer = new byte[partialContentStream.Length];
                        _ = await partialContentStream.ReadAsync(buffer);

                        var offset = buffer.Length / 2;
                        var chunkToRepeat = new ReadOnlyMemory<byte>(buffer, offset, buffer.Length - offset);
                        await using var tempStream = new MemoryStream();
                        await tempStream.WriteAsync(chunkToRepeat);

                        while (tempStream.Length < response.Content.Headers.ContentLength.Value)
                        {
                            await tempStream.WriteAsync(chunkToRepeat);
                            if (tempStream.Length >= response.Content.Headers.ContentLength.Value)
                            {
                                tempStream.SetLength(response.Content.Headers.ContentLength.Value);
                                break;
                            }
                        }

                        tempStream.Position = 0;

                        using var tempDecoder = AudioEngine.Instance.CreateDecoder(tempStream);
                        Length = tempDecoder.Length;
                    }
                    catch
                    {
                        Length = -1;
                    }
                }
                else
                {
                    Length = -1;
                }


                var networkStream = await response.Content.ReadAsStreamAsync();
                _stream = new MemoryStream();
                await networkStream.CopyToAsync(_stream);
                _stream.Position = 0;

                _decoder = AudioEngine.Instance.CreateDecoder(_stream);
                SampleFormat = _decoder.SampleFormat;

                _cancellationTokenSource = new CancellationTokenSource();
                _contentLength = response.Content.Headers.ContentLength;
                _ = Task.Run(() => BufferDirectStreamAsync(_cancellationTokenSource.Token));
            }
            catch
            {
                lock (_lock)
                {
                    _isEndOfStream = true;
                    Monitor.PulseAll(_lock);
                }
            }
        });
    }

    private void InitializeHlsStream()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        Task.Run(async () =>
        {
            try
            {
                await DownloadAndParsePlaylistAsync(_url, _cancellationTokenSource.Token);
                if (_hlsSegments.Count == 0)
                {
                    throw new InvalidOperationException("No segments found in HLS playlist.");
                }

                SampleFormat = SampleFormat.F32;
                Length = _isEndList ? (int)(_hlsTotalDuration * SampleRate) : -1;
                CanSeek = _isEndList;
                await BufferHlsStreamAsync(_cancellationTokenSource.Token);
            }
            catch
            {
                lock (_lock)
                {
                    _isEndOfStream = true;
                    Monitor.PulseAll(_lock);
                }
            }
        });
    }

    private void BufferDirectStreamAsync(CancellationToken cancellationToken)
    {
        try
        {
            var buffer = ArrayPool<float>.Shared.Rent(8192);

            try
            {
                while (!IsDisposed && !cancellationToken.IsCancellationRequested)
                {
                    var samplesRead = _decoder!.Decode(buffer);

                    if (samplesRead > 0)
                    {
                        lock (_lock)
                        {
                            for (var i = 0; i < samplesRead; i++)
                            {
                                _audioBuffer.Enqueue(buffer[i]);
                            }

                            Monitor.PulseAll(_lock);
                        }
                    }
                    else
                    {
                        lock (_lock)
                        {
                            _isEndOfStream = true;
                            Monitor.PulseAll(_lock);
                        }

                        break;
                    }
                }
            }
            finally
            {
                ArrayPool<float>.Shared.Return(buffer);
            }
        }
        catch
        {
            lock (_lock)
            {
                _isEndOfStream = true;
                Monitor.PulseAll(_lock);
            }
        }
    }

    private async Task DownloadAndParsePlaylistAsync(string url, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        ParseHlsPlaylist(content, url);
    }

    private void ParseHlsPlaylist(string playlistContent, string baseUrl)
    {
        var lines = playlistContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        double segmentDuration = 0;
        _hlsSegments.Clear();
        _hlsTotalDuration = 0;
        _isEndList = false;
        _hlsTargetDuration = 5;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            if (trimmedLine.StartsWith("#EXT-X-TARGETDURATION", StringComparison.OrdinalIgnoreCase))
            {
                var durationStr = trimmedLine["#EXT-X-TARGETDURATION:".Length..];
                if (double.TryParse(durationStr, out var duration))
                    _hlsTargetDuration = duration;
            }
            else if (trimmedLine.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                var durationStr = trimmedLine["#EXTINF:".Length..].Split(',')[0];
                if (double.TryParse(durationStr, out var duration))
                    segmentDuration = duration;
                else
                    segmentDuration = 0;
            }
            else if (trimmedLine.StartsWith("#EXT-X-ENDLIST", StringComparison.OrdinalIgnoreCase))
            {
                _isEndList = true;
            }
            else if (!trimmedLine.StartsWith('#'))
            {
                var segmentUri = CombineUri(baseUrl, trimmedLine);
                _hlsSegments.Add(new HlsSegment
                {
                    Uri = segmentUri,
                    Duration = segmentDuration
                });
                _hlsTotalDuration += segmentDuration;
                segmentDuration = 0;
            }
        }
    }

    private static string CombineUri(string baseUri, string relativeUri)
    {
        if (!Uri.TryCreate(baseUri, UriKind.Absolute, out var baseUriObj)) return relativeUri;
        return Uri.TryCreate(baseUriObj, relativeUri, out var newUri) ? newUri.ToString() : relativeUri;
    }

    private async Task BufferHlsStreamAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!IsDisposed && !cancellationToken.IsCancellationRequested)
            {
                if (!_isEndList && ShouldRefreshPlaylist())
                {
                    _lastPlaylistRefreshTime = DateTime.UtcNow;
                    await DownloadAndParsePlaylistAsync(_url, cancellationToken);
                }

                if (_currentSegmentIndex < _hlsSegments.Count)
                {
                    var segment = _hlsSegments[_currentSegmentIndex];
                    await DownloadAndBufferSegmentAsync(segment, cancellationToken);
                    _currentSegmentIndex++;
                }
                else if (_isEndList)
                {
                    lock (_lock)
                    {
                        _isEndOfStream = true;
                        Monitor.PulseAll(_lock);
                    }

                    EndOfStreamReached?.Invoke(this, EventArgs.Empty);
                    break;
                }
                else
                {
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }
        catch
        {
            lock (_lock)
            {
                _isEndOfStream = true;
                Monitor.PulseAll(_lock);
            }
        }
        finally
        {
            DisposeResources();
        }
    }

    private bool ShouldRefreshPlaylist()
    {
        if (_isEndList)
            return false;

        var elapsed = DateTime.UtcNow - _lastPlaylistRefreshTime;
        // Refresh the playlist a bit before the target duration to be safe (e.g., 80% of target duration)
        var refreshInterval = TimeSpan.FromSeconds(_hlsTargetDuration * 0.8);
        return elapsed >= refreshInterval;
    }

    private async Task DownloadAndBufferSegmentAsync(HlsSegment segment, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(segment.Uri, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            await using var segmentStream = await response.Content.ReadAsStreamAsync(cancellationToken);

            if (_decoder == null)
            {
                _decoder = AudioEngine.Instance.CreateDecoder(segmentStream);
                SampleFormat = _decoder.SampleFormat;
            }

            var buffer = ArrayPool<float>.Shared.Rent(8192);

            try
            {
                while (!IsDisposed && !cancellationToken.IsCancellationRequested)
                {
                    var samplesRead = _decoder.Decode(buffer);

                    if (samplesRead > 0)
                    {
                        lock (_lock)
                        {
                            for (var i = 0; i < samplesRead; i++)
                            {
                                _audioBuffer.Enqueue(buffer[i]);
                            }

                            Monitor.PulseAll(_lock);
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
            finally
            {
                ArrayPool<float>.Shared.Return(buffer);
            }
        }
        catch
        {
            // ignored
        }
    }

    private async void SeekInDirectStream(int sampleOffset)
    {
        try
        {
            if (_decoder == null || _stream == null) return;
            if (!CanSeek || !_contentLength.HasValue || Length <= 0) return;

            var timeProportion = (float)sampleOffset / Length;
            var targetByteOffset = (long)(timeProportion * _contentLength.Value);
            targetByteOffset = Math.Max(0, targetByteOffset);
            targetByteOffset = Math.Min(targetByteOffset, _contentLength.Value - 1);


            if (sampleOffset < _samplePosition) // Backward Seek
            {
                _stream.Seek(targetByteOffset, SeekOrigin.Begin);
                _decoder.Seek(sampleOffset);
                _samplePosition = sampleOffset;
                _audioBuffer.Clear();
                PositionChanged?.Invoke(this, new PositionChangedEventArgs(_samplePosition));
            }
            else // Forward Seek (or same position)
            {
                if (targetByteOffset >= _stream.Length)
                {
                    var currentStreamLength = _stream.Length;
                    var endByteToFetch = Math.Min(targetByteOffset + 1024 * 1024,
                        _contentLength.GetValueOrDefault(long.MaxValue) - 1);
                    if (endByteToFetch < currentStreamLength) endByteToFetch = currentStreamLength + (1024 * 1024);

                    using (var rangeRequest = new HttpRequestMessage(HttpMethod.Get, _url))
                    {
                        rangeRequest.Headers.Range = new RangeHeaderValue(currentStreamLength, endByteToFetch);

                        using (var rangeResponse = await _httpClient.SendAsync(rangeRequest, HttpCompletionOption.ResponseContentRead))
                        {
                            rangeResponse.EnsureSuccessStatusCode();
                            await using (var contentStream = await rangeResponse.Content.ReadAsStreamAsync())
                            {
                                await contentStream.CopyToAsync(_stream);
                            }
                        }
                    }

                    _stream.Position = targetByteOffset;
                    _decoder.Seek(sampleOffset);
                    _samplePosition = sampleOffset;
                    _audioBuffer.Clear();
                    PositionChanged?.Invoke(this, new PositionChangedEventArgs(_samplePosition));
                }
                else // Forward seek within current stream (or backward seek, if the condition is not met)
                {
                    _stream.Seek(targetByteOffset, SeekOrigin.Begin);
                    _decoder.Seek(sampleOffset);
                    _samplePosition = sampleOffset;
                    _audioBuffer.Clear();
                    PositionChanged?.Invoke(this, new PositionChangedEventArgs(_samplePosition));
                }
            }

            _ = Task.Run(() => BufferDirectStreamAsync(CancellationToken.None));
        }
        catch
        {
            _isEndOfStream = true;
            Monitor.PulseAll(_lock);
        }
    }

    private void SeekInHlsStream(int sampleOffset)
    {
        var targetTime = sampleOffset / (double)SampleRate;

        double cumulativeTime = 0;
        var index = 0;
        foreach (var segment in _hlsSegments)
        {
            cumulativeTime += segment.Duration;
            if (cumulativeTime >= targetTime)
                break;

            index++;
        }

        if (index >= _hlsSegments.Count)
            index = _hlsSegments.Count - 1;

        _currentSegmentIndex = index;

        lock (_lock)
        {
            _decoder?.Dispose();
            _audioBuffer.Clear();
            _samplePosition = sampleOffset;
            PositionChanged?.Invoke(this, new PositionChangedEventArgs(_samplePosition));
        }

        _cancellationTokenSource?.Cancel(false);
        _cancellationTokenSource = new CancellationTokenSource();
        Task.Run(async () => { await BufferHlsStreamAsync(_cancellationTokenSource.Token); });
    }

    private void DisposeResources()
    {
        _decoder?.Dispose();
        _stream?.Dispose();
        _cancellationTokenSource?.Cancel(false);
        _cancellationTokenSource?.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (IsDisposed)
            return;

        lock (_lock)
        {
            IsDisposed = true;
            _httpClient.Dispose();
            DisposeResources();
            _audioBuffer.Clear();
        }
    }

    private class HlsSegment
    {
        public string Uri { get; init; } = string.Empty;
        public double Duration { get; init; }
    }
}