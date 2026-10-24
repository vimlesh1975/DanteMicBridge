using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DanteMicBridge.Core
{
    /// <summary>
    /// VB-Audio VBAN (VB-Audio Network) UDP Audio Protocol Transmitter.
    /// Completely free, open specification, sub-10ms latency audio streaming.
    /// Broadcasts microphone PCM packets directly over local LAN/Wi-Fi to any PC running VBAN Receptor.
    /// </summary>
    public class VbanAudioTransmitter : IDanteAudioTransmitter
    {
        private Socket? _udpSocket;
        private IPEndPoint? _targetEndPoint;
        private bool _isConnected;
        private string _streamName = "Stream1";
        private string _targetIp = "255.255.255.255"; // Default: Broadcast to entire LAN
        private int _port = 6980; // Standard VBAN port
        private int _sampleRate = 48000;
        private int _bitDepth = 16;
        private byte _packetCounter;

        // VBAN standard packet: up to 256 samples per packet (typically 64..256 for ultra-low latency)
        private const int VbanSamplesPerPacket = 128;
        private readonly byte[] _packetBuffer;
        private int _bufferIndex;
        private readonly float[] _accumL = new float[VbanSamplesPerPacket];
        private readonly float[] _accumR = new float[VbanSamplesPerPacket];

        public bool IsConnected => _isConnected;
        public string ChannelName => _streamName;
        public int SampleRate => _sampleRate;
        public int BitDepth => _bitDepth;
        public string TargetIp => _targetIp;
        public int Port => _port;

        public event Action<string>? OnLog;

        public VbanAudioTransmitter()
        {
            // VBAN Header = 28 bytes. 
            // 256 samples * 2 channels * 2 bytes (16-bit PCM) = 1024 bytes payload.
            _packetBuffer = new byte[28 + 2048];
        }

        public bool Initialize(string streamName, int sampleRate = 48000, int bitDepth = 16)
        {
            return Initialize(streamName, sampleRate, bitDepth, "255.255.255.255", 6980);
        }

        public bool Initialize(string streamName, int sampleRate, int bitDepth, string targetIp, int port)
        {
            Stop();

            _streamName = string.IsNullOrWhiteSpace(streamName) ? "Stream1" : streamName.Trim();
            _sampleRate = sampleRate;
            _bitDepth = 16; // Standard VBAN Receptor supports 16-bit and 24-bit PCM; 16-bit gives maximum compatibility
            _targetIp = string.IsNullOrWhiteSpace(targetIp) ? "255.255.255.255" : targetIp.Trim();
            _port = port > 0 ? port : 6980;

            try
            {
                _udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _udpSocket.EnableBroadcast = true;
                _targetEndPoint = new IPEndPoint(IPAddress.Parse(_targetIp), _port);

                _isConnected = true;
                _bufferIndex = 0;
                _packetCounter = 0;

                OnLog?.Invoke($"[VBAN LAN Streamer] Online. Target: {_targetIp}:{_port}, StreamName: '{_streamName}', Rate: {_sampleRate}Hz 16-bit Stereo.");
                OnLog?.Invoke($"[VBAN LAN Streamer] Open 'VBAN Receptor' on PC 2 and set stream name to '{_streamName}'.");
                return true;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[VBAN Error] Failed to open UDP socket: {ex.Message}");
                _isConnected = false;
                return false;
            }
        }

        public void TransmitAudioSamples(float[] leftChannel, float[]? rightChannel, int count)
        {
            if (!_isConnected || _udpSocket == null || _targetEndPoint == null || count <= 0) return;

            int src = 0;
            while (src < count)
            {
                int needed = VbanSamplesPerPacket - _bufferIndex;
                int toCopy = Math.Min(needed, count - src);

                Array.Copy(leftChannel, src, _accumL, _bufferIndex, toCopy);
                if (rightChannel != null)
                {
                    Array.Copy(rightChannel, src, _accumR, _bufferIndex, toCopy);
                }
                else
                {
                    Array.Copy(leftChannel, src, _accumR, _bufferIndex, toCopy);
                }

                _bufferIndex += toCopy;
                src += toCopy;

                if (_bufferIndex >= VbanSamplesPerPacket)
                {
                    SendVbanPacket();
                    _bufferIndex = 0;
                }
            }
        }

        private void SendVbanPacket()
        {
            if (_udpSocket == null || _targetEndPoint == null) return;

            // --- VBAN PROTOCOL HEADER (28 Bytes) ---
            // 0..3: 'V', 'B', 'A', 'N' (FourCC)
            _packetBuffer[0] = (byte)'V';
            _packetBuffer[1] = (byte)'B';
            _packetBuffer[2] = (byte)'A';
            _packetBuffer[3] = (byte)'N';

            // 4: Sample rate index (VBAN sub-protocol bit 0..4 = rate index, bit 5..7 = protocol audio 0x00)
            _packetBuffer[4] = GetVbanSampleRateIndex(_sampleRate);

            // 5: Samples per packet - 1 (128 samples - 1 = 127)
            _packetBuffer[5] = (byte)(VbanSamplesPerPacket - 1);

            // 6: Channels - 1 (2 channels - 1 = 1)
            _packetBuffer[6] = 1;

            // 7: Data format (0x01 = 16-bit PCM Signed Integer, Little-Endian)
            _packetBuffer[7] = 0x01;

            // 8..23: Stream Name (16 bytes ASCII null-padded)
            byte[] nameBytes = Encoding.ASCII.GetBytes(_streamName);
            for (int i = 0; i < 16; i++)
            {
                _packetBuffer[8 + i] = (i < nameBytes.Length) ? nameBytes[i] : (byte)0;
            }

            // 24..27: Frame Counter (uint32 Little-Endian)
            _packetBuffer[24] = _packetCounter;
            _packetBuffer[25] = 0;
            _packetBuffer[26] = 0;
            _packetBuffer[27] = 0;
            _packetCounter++;

            // --- AUDIO PAYLOAD (16-bit Stereo Interleaved Little-Endian) ---
            int offset = 28;
            for (int i = 0; i < VbanSamplesPerPacket; i++)
            {
                // Left Sample
                short sampleL = (short)(Math.Clamp(_accumL[i], -1.0f, 1.0f) * 32767.0f);
                _packetBuffer[offset++] = (byte)(sampleL & 0xFF);
                _packetBuffer[offset++] = (byte)((sampleL >> 8) & 0xFF);

                // Right Sample
                short sampleR = (short)(Math.Clamp(_accumR[i], -1.0f, 1.0f) * 32767.0f);
                _packetBuffer[offset++] = (byte)(sampleR & 0xFF);
                _packetBuffer[offset++] = (byte)((sampleR >> 8) & 0xFF);
            }

            try
            {
                _udpSocket.SendTo(_packetBuffer, 0, offset, SocketFlags.None, _targetEndPoint);
            }
            catch
            {
                // Ignore transient network socket drops
            }
        }

        private static byte GetVbanSampleRateIndex(int rate)
        {
            return rate switch
            {
                6000 => 0,
                12000 => 1,
                24000 => 2,
                48000 => 3,
                96000 => 4,
                192000 => 5,
                384000 => 6,
                8000 => 7,
                16000 => 8,
                32000 => 9,
                64000 => 10,
                128000 => 11,
                256000 => 12,
                512000 => 13,
                11025 => 14,
                22050 => 15,
                44100 => 16,
                88200 => 17,
                176400 => 18,
                352800 => 19,
                705600 => 20,
                _ => 3 // default 48kHz
            };
        }

        public void Stop()
        {
            _isConnected = false;
            if (_udpSocket != null)
            {
                try
                {
                    _udpSocket.Close();
                    _udpSocket.Dispose();
                }
                catch { }
                _udpSocket = null;
            }
            OnLog?.Invoke("[VBAN LAN Streamer] Transmitter stopped.");
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
