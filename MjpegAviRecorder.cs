using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;

namespace GabelstaplerKameraMonitor
{
    public sealed class MjpegAviRecorder : IDisposable
    {
        private readonly FileStream _stream;
        private readonly BinaryWriter _writer;
        private readonly List<IndexEntry> _index = new List<IndexEntry>();
        private readonly ImageCodecInfo _jpegCodec;
        private readonly EncoderParameters _encoderParameters;
        private readonly int _fps;
        private readonly int _width;
        private readonly int _height;
        private readonly int _suggestedBufferSize;

        private long _riffSizePosition;
        private long _totalFramesPosition;
        private long _streamLengthPosition;
        private long _moviSizePosition;
        private long _moviListStart;
        private bool _closed;

        public MjpegAviRecorder(string path, int width, int height, int fps, long jpegQuality)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Invalid recording file path.", nameof(path));

            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Recording size must be greater than zero.");

            _width = width;
            _height = height;
            _fps = Math.Max(1, fps);
            _suggestedBufferSize = Math.Max(1024, width * height * 3);
            _jpegCodec = ImageCodecInfo.GetImageEncoders().First(x => x.FormatID == ImageFormat.Jpeg.Guid);
            _encoderParameters = new EncoderParameters(1);
            _encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, Math.Max(1, Math.Min(100, jpegQuality)));

            _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            _writer = new BinaryWriter(_stream, Encoding.ASCII);

            WriteHeader();
        }

        public int Width => _width;
        public int Height => _height;
        public int FrameCount => _index.Count;

        public void WriteFrame(Bitmap bitmap)
        {
            if (_closed)
                throw new ObjectDisposedException(nameof(MjpegAviRecorder));

            if (bitmap == null)
                return;

            using (var frame = PrepareFrame(bitmap))
            using (var memory = new MemoryStream())
            {
                frame.Save(memory, _jpegCodec, _encoderParameters);
                var data = memory.ToArray();

                var offset = (uint)(_stream.Position - _moviListStart - 4);

                WriteFourCc("00dc");
                _writer.Write(data.Length);
                _writer.Write(data);

                if ((data.Length & 1) == 1)
                    _writer.Write((byte)0);

                _index.Add(new IndexEntry(offset, (uint)data.Length));
            }
        }

        public void Dispose()
        {
            Close();
        }

        public void Close()
        {
            if (_closed)
                return;

            _closed = true;

            var indexStart = _stream.Position;

            WriteFourCc("idx1");
            _writer.Write(_index.Count * 16);

            foreach (var item in _index)
            {
                WriteFourCc("00dc");
                _writer.Write(0x10);
                _writer.Write(item.Offset);
                _writer.Write(item.Size);
            }

            var fileEnd = _stream.Position;

            PatchUInt32(_riffSizePosition, (uint)(fileEnd - 8));
            PatchUInt32(_totalFramesPosition, (uint)_index.Count);
            PatchUInt32(_streamLengthPosition, (uint)_index.Count);
            PatchUInt32(_moviSizePosition, (uint)(indexStart - _moviSizePosition - 4));

            _writer.Flush();
            _writer.Dispose();
            _stream.Dispose();
            _encoderParameters.Dispose();
        }

        private Bitmap PrepareFrame(Bitmap source)
        {
            if (source.Width == _width && source.Height == _height)
                return new Bitmap(source);

            var resized = new Bitmap(_width, _height);

            using (var graphics = Graphics.FromImage(resized))
            {
                graphics.DrawImage(source, 0, 0, _width, _height);
            }

            return resized;
        }

        private void WriteHeader()
        {
            WriteFourCc("RIFF");
            _riffSizePosition = _stream.Position;
            _writer.Write(0);
            WriteFourCc("AVI ");

            BeginList("hdrl", out var hdrlSizePosition);
            WriteMainAviHeader();
            BeginList("strl", out var strlSizePosition);
            WriteStreamHeader();
            WriteStreamFormat();
            EndList(strlSizePosition);
            EndList(hdrlSizePosition);

            BeginList("movi", out _moviSizePosition);
            _moviListStart = _stream.Position - 4;
        }

        private void WriteMainAviHeader()
        {
            BeginChunk("avih", out var sizePosition);

            _writer.Write((uint)(1000000 / _fps));
            _writer.Write((uint)(_suggestedBufferSize * _fps));
            _writer.Write((uint)0);
            _writer.Write((uint)0x10);

            _totalFramesPosition = _stream.Position;
            _writer.Write((uint)0);

            _writer.Write((uint)0);
            _writer.Write((uint)1);
            _writer.Write((uint)_suggestedBufferSize);
            _writer.Write((uint)_width);
            _writer.Write((uint)_height);
            _writer.Write((uint)0);
            _writer.Write((uint)0);
            _writer.Write((uint)0);
            _writer.Write((uint)0);

            EndChunk(sizePosition);
        }

        private void WriteStreamHeader()
        {
            BeginChunk("strh", out var sizePosition);

            WriteFourCc("vids");
            WriteFourCc("MJPG");
            _writer.Write((uint)0);
            _writer.Write((ushort)0);
            _writer.Write((ushort)0);
            _writer.Write((uint)0);
            _writer.Write((uint)1);
            _writer.Write((uint)_fps);
            _writer.Write((uint)0);

            _streamLengthPosition = _stream.Position;
            _writer.Write((uint)0);

            _writer.Write((uint)_suggestedBufferSize);
            _writer.Write((uint)0xFFFFFFFF);
            _writer.Write((uint)0);
            _writer.Write((short)0);
            _writer.Write((short)0);
            _writer.Write((short)_width);
            _writer.Write((short)_height);

            EndChunk(sizePosition);
        }

        private void WriteStreamFormat()
        {
            BeginChunk("strf", out var sizePosition);

            _writer.Write((uint)40);
            _writer.Write((int)_width);
            _writer.Write((int)_height);
            _writer.Write((ushort)1);
            _writer.Write((ushort)24);
            WriteFourCc("MJPG");
            _writer.Write((uint)_suggestedBufferSize);
            _writer.Write((int)0);
            _writer.Write((int)0);
            _writer.Write((uint)0);
            _writer.Write((uint)0);

            EndChunk(sizePosition);
        }

        private void BeginList(string name, out long sizePosition)
        {
            WriteFourCc("LIST");
            sizePosition = _stream.Position;
            _writer.Write(0);
            WriteFourCc(name);
        }

        private void EndList(long sizePosition)
        {
            var end = _stream.Position;
            PatchUInt32(sizePosition, (uint)(end - sizePosition - 4));
            _stream.Position = end;
        }

        private void BeginChunk(string name, out long sizePosition)
        {
            WriteFourCc(name);
            sizePosition = _stream.Position;
            _writer.Write(0);
        }

        private void EndChunk(long sizePosition)
        {
            var end = _stream.Position;
            var size = end - sizePosition - 4;
            PatchUInt32(sizePosition, (uint)size);
            _stream.Position = end;

            if ((size & 1) == 1)
                _writer.Write((byte)0);
        }

        private void PatchUInt32(long position, uint value)
        {
            var current = _stream.Position;
            _stream.Position = position;
            _writer.Write(value);
            _stream.Position = current;
        }

        private void WriteFourCc(string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);

            if (bytes.Length != 4)
                throw new ArgumentException("FourCC must be four characters long.", nameof(value));

            _writer.Write(bytes);
        }

        private struct IndexEntry
        {
            public IndexEntry(uint offset, uint size)
            {
                Offset = offset;
                Size = size;
            }

            public uint Offset { get; }
            public uint Size { get; }
        }
    }
}
