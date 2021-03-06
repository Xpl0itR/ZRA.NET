﻿// SPDX-License-Identifier: BSD-3-Clause
// Copyright © 2020 ZRA Contributors (https://github.com/zraorg)

using System;
using System.Buffers;
using System.IO;

namespace ZRA.NET.Streaming
{
    public class ZraCompressionStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _outStream.Length;
        public override long Position { get => _outStream.Position; set => throw new NotSupportedException(); }

        private readonly IntPtr _compressor;
        private readonly Stream _outStream;
        private readonly bool   _leaveOpen;
        private readonly long   _startingPos;
        private readonly ulong  _headerLength;

        /**
         * <summary>
         * Creates a <see cref="ZraCompressionStream"/> used to stream-compress data.
         * </summary>
         * <param name="outStream">The underlying stream where the compressed data will be written to.</param>
         * <param name="inLength">The length of data that is going to be compressed.</param>
         * <param name="compressionLevel">The level of ZSTD compression to use.</param>
         * <param name="frameSize">The size of a single frame which can be decompressed individually.</param>
         * <param name="leaveOpen">Whether to leave the underlying stream open or not when the <see cref="ZraCompressionStream"/> is disposed.</param>
         * <param name="metaBuffer"> A buffer containing the metadata</param>
         * <returns><see cref="ZraCompressionStream"/></returns>
         */
        public ZraCompressionStream(Stream outStream, ulong inLength, byte compressionLevel, uint frameSize, byte[] metaBuffer = null, bool leaveOpen = false)
        {
            _outStream   = outStream;
            _leaveOpen   = leaveOpen;
            _startingPos = _outStream.Position;

            ulong metaSize = metaBuffer == null ? 0 : (ulong)metaBuffer.Length;

            LibZra.ZraCreateCompressor(out _compressor, inLength, compressionLevel, frameSize, true, metaBuffer, metaSize).ThrowIfError();

            _headerLength       = LibZra.ZraGetHeaderSizeWithCompressor(_compressor);
            _outStream.Position = _startingPos + (long)_headerLength;
        }

        /**
         * <summary>
         * Compresses a sequence of bytes and then writes it to the current stream and advances the current position within this stream by the number of bytes written.
         * </summary>
         * <remarks>After all the input data is compressed and written, you must call <see cref="Dispose"/> to write the ZRA header to the output stream.</remarks>
         * <param name="buffer">A byte array containing the data to be compressed and written.</param>
         * <param name="offset">The zero-based byte offset in buffer at which to begin compressing bytes and writing them to the current stream.</param>
         * <param name="count">The number of bytes to be compressed and written to the current stream.</param>
         */
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (offset > 0) Array.Copy(buffer, offset, buffer, 0, count);

            byte[] outputBuffer = ArrayPool<byte>.Shared.Rent((int)LibZra.ZraGetOutputBufferSizeWithCompressor(_compressor, (ulong)buffer.LongLength));
            LibZra.ZraCompressWithCompressor(_compressor, buffer, (ulong)count, outputBuffer, out ulong outputSize).ThrowIfError();

            _outStream.Write(outputBuffer, 0, (int)outputSize);

            ArrayPool<byte>.Shared.Return(outputBuffer);
        }

        /**
         * <summary>
         * Writes the ZRA header to the underlying stream and then releases the unmanaged resources
         * used by the <see cref="ZraCompressionStream"/> and optionally releases the managed resources.
         * </summary>
         */
        protected override void Dispose(bool disposing)
        {
            byte[] headerBuffer = ArrayPool<byte>.Shared.Rent((int)_headerLength);
            LibZra.ZraGetHeaderWithCompressor(_compressor, headerBuffer).ThrowIfError();

            _outStream.Position = _startingPos;
            _outStream.Write(headerBuffer, 0, (int)_headerLength);

            ArrayPool<byte>.Shared.Return(headerBuffer);
            LibZra.ZraDeleteCompressor(_compressor);

            if (!_leaveOpen)
                _outStream?.Dispose();

            base.Dispose(disposing);
        }

        public override void Flush() => _outStream.Flush();

        /**<summary>Not Supported.</summary>*/
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /**<summary>Not Supported.</summary>*/
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /**<summary>Not Supported.</summary>*/
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}