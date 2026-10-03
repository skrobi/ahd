using System.IO;
using System.IO.Compression;

namespace PzlEv.Shared.Utils.Files;

/// <summary>Kompresja treści pliku do zapisu w bazie (GZip – ten sam format co COMPRESS / DECOMPRESS w SQL Server).</summary>
public static class FileCompression
{
    public static byte[] GUnzip(byte[] compressed)
    {
        using var input = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>
    /// Strumień do odczytu treści source skompresowanej GZip w locie – zapis dużego pliku do bazy bez jego kopii
    /// w pamięci ani pliku pośredniego. Zamknięcie zamyka source.
    /// </summary>
    public static Stream GZipReading(Stream source) => new CompressingStream(source);

    private sealed class CompressingStream(Stream source) : Stream
    {
        private readonly MemoryStream _output = new();
        private readonly byte[] _chunk = new byte[1 << 16];
        private GZipStream? _gzip;
        private int _offset;
        private bool _finished;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            _gzip ??= new GZipStream(_output, CompressionLevel.Optimal, leaveOpen: true);
            while (_output.Length == _offset && !_finished)
            {
                _output.SetLength(0);
                _output.Position = 0;
                _offset = 0;
                var read = source.Read(_chunk);
                if (read == 0)
                {
                    _gzip.Dispose();   // reszta danych i stopka GZip
                    _finished = true;
                }
                else
                    _gzip.Write(_chunk, 0, read);
            }
            var count = Math.Min((int)_output.Length - _offset, buffer.Length);
            _output.GetBuffer().AsSpan(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _gzip?.Dispose();
                _output.Dispose();
                source.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
