using System.Buffers;
using SkiaSharp;

namespace Jellyfin.Plugin.SubtitleFontBridge.Services;

/// <summary>
/// Adapts a seekable Skia font asset to a read-only .NET stream.
/// </summary>
internal sealed class SkiaFontStream : Stream
{
    private readonly SKFontStyleSet _styleSet;
    private readonly SKTypeface _typeface;
    private readonly SKStreamAsset _stream;
    private bool _disposed;

    public SkiaFontStream(
        SKFontStyleSet styleSet,
        SKTypeface typeface,
        SKStreamAsset stream)
    {
        _styleSet = styleSet;
        _typeface = typeface;
        _stream = stream;
    }

    public override bool CanRead => !_disposed;

    public override bool CanSeek => !_disposed;

    public override bool CanWrite => false;

    public override long Length
    {
        get
        {
            ThrowIfDisposed();
            return _stream.Length;
        }
    }

    public override long Position
    {
        get
        {
            ThrowIfDisposed();
            return _stream.Position;
        }

        set => Seek(value, SeekOrigin.Begin);
    }

    public override void Flush()
    {
        ThrowIfDisposed();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset > buffer.Length - count)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        ThrowIfDisposed();
        if (count == 0)
        {
            return 0;
        }

        if (offset == 0)
        {
            return _stream.Read(buffer, count);
        }

        var temporary = ArrayPool<byte>.Shared.Rent(count);
        try
        {
            var read = _stream.Read(temporary, count);
            temporary.AsSpan(0, read).CopyTo(buffer.AsSpan(offset, read));
            return read;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(temporary);
        }
    }

    public override int Read(Span<byte> buffer)
    {
        ThrowIfDisposed();
        if (buffer.IsEmpty)
        {
            return 0;
        }

        var temporary = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            var read = _stream.Read(temporary, buffer.Length);
            temporary.AsSpan(0, read).CopyTo(buffer);
            return read;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(temporary);
        }
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(buffer.Span));
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ThrowIfDisposed();

        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (target < 0 || target > Length || target > int.MaxValue)
        {
            throw new IOException("The requested font stream position is outside the resource.");
        }

        if (!_stream.Seek(checked((int)target)))
        {
            throw new IOException("Skia could not seek the font stream.");
        }

        return target;
    }

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _stream.Dispose();
            _typeface.Dispose();
            _styleSet.Dispose();
            _disposed = true;
        }

        base.Dispose(disposing);
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
