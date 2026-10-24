using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DanteMicBridge.Core
{
    /// <summary>
    /// Plays the captured microphone into a Dante Virtual Soundcard (DVS) WASAPI output device.
    /// DVS then publishes those channels on the Dante network, so the PC appears in Dante Controller.
    /// Mic left/right go to output channels 1/2 (route them in DVS / Dante Controller).
    /// </summary>
    public class DvsAudioTransmitter : IDanteAudioTransmitter
    {
        private WasapiOut? _output;
        private BufferedWaveProvider? _buffer;
        private bool _isConnected;
        private string _channelName = "Mic-In";
        private int _sampleRate = 48000;
        private int _bitDepth = 24;
        private int _outRate = 48000;
        private int _outChannels = 2;

        // Linear resampler state (used only if capture rate != DVS rate)
        private double _srcPos;
        private float _prevL, _prevR;
        private float[] _scratch = new float[0];

        public bool IsConnected => _isConnected;
        public string ChannelName => _channelName;
        public int SampleRate => _sampleRate;
        public int BitDepth => _bitDepth;

        public event Action<string>? OnLog;

        /// <summary>Render devices that look like Dante Virtual Soundcard (listed first), then all others.</summary>
        public static List<MMDevice> GetOutputDevices()
        {
            using var enumerator = new MMDeviceEnumerator();
            var all = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
            return all.OrderByDescending(d => d.FriendlyName.Contains("Dante", StringComparison.OrdinalIgnoreCase))
                      .ToList();
        }

        public bool Initialize(string channelName, int sampleRate = 48000, int bitDepth = 24)
        {
            var dev = GetOutputDevices().FirstOrDefault(d => d.FriendlyName.Contains("Dante", StringComparison.OrdinalIgnoreCase));
            if (dev == null)
            {
                OnLog?.Invoke("[DVS] No Dante Virtual Soundcard output device found. Install and start DVS first.");
                return false;
            }
            return Initialize(channelName, sampleRate, bitDepth, dev);
        }

        public bool Initialize(string channelName, int sampleRate, int bitDepth, MMDevice device)
        {
            Stop();
            _channelName = channelName;
            _sampleRate = sampleRate;
            _bitDepth = bitDepth;

            try
            {
                var mix = device.AudioClient.MixFormat;
                _outRate = mix.SampleRate;
                _outChannels = Math.Max(2, mix.Channels);

                var fmt = WaveFormat.CreateIeeeFloatWaveFormat(_outRate, _outChannels);
                _buffer = new BufferedWaveProvider(fmt)
                {
                    BufferDuration = TimeSpan.FromMilliseconds(250),
                    DiscardOnBufferOverflow = true
                };

                _output = new WasapiOut(device, AudioClientShareMode.Shared, true, 20);
                _output.Init(_buffer);
                _output.Play();

                _srcPos = 0; _prevL = 0; _prevR = 0;
                _isConnected = true;
                OnLog?.Invoke($"[DVS] Playing into '{device.FriendlyName}' ({_outRate} Hz, {_outChannels} ch). Mic L/R -> output ch 1/2.");
                if (_sampleRate != _outRate)
                    OnLog?.Invoke($"[DVS] Resampling {_sampleRate} Hz -> {_outRate} Hz (set DVS to match for best quality).");
                return true;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[DVS Error] Failed to open '{device.FriendlyName}': {ex.Message}");
                Stop();
                return false;
            }
        }

        public void TransmitAudioSamples(float[] leftChannel, float[]? rightChannel, int count)
        {
            if (!_isConnected || _buffer == null || count <= 0) return;

            int outFrames;
            if (_sampleRate == _outRate)
            {
                outFrames = count;
                EnsureScratch(outFrames);
                for (int i = 0; i < count; i++)
                {
                    float l = leftChannel[i];
                    float r = rightChannel != null ? rightChannel[i] : l;
                    WriteFrame(i, l, r);
                }
            }
            else
            {
                double step = (double)_sampleRate / _outRate;
                int maxFrames = (int)(count / step) + 2;
                EnsureScratch(maxFrames);
                outFrames = 0;
                while (true)
                {
                    int idx = (int)Math.Floor(_srcPos);
                    if (idx + 1 >= count) break;
                    double frac = _srcPos - idx;
                    float l0 = idx < 0 ? _prevL : leftChannel[idx];
                    float r0 = idx < 0 ? _prevR : (rightChannel != null ? rightChannel[idx] : leftChannel[idx]);
                    float l1 = leftChannel[idx + 1];
                    float r1 = rightChannel != null ? rightChannel[idx + 1] : l1;
                    WriteFrame(outFrames++, (float)(l0 + (l1 - l0) * frac), (float)(r0 + (r1 - r0) * frac));
                    _srcPos += step;
                    if (outFrames >= maxFrames) break;
                }
                _srcPos -= count;
                _prevL = leftChannel[count - 1];
                _prevR = rightChannel != null ? rightChannel[count - 1] : _prevL;
            }

            var bytes = new byte[outFrames * _outChannels * 4];
            Buffer.BlockCopy(_scratch, 0, bytes, 0, bytes.Length);
            _buffer.AddSamples(bytes, 0, bytes.Length);
        }

        private void EnsureScratch(int frames)
        {
            int needed = frames * _outChannels;
            if (_scratch.Length < needed) _scratch = new float[needed];
            else Array.Clear(_scratch, 0, needed);
        }

        private void WriteFrame(int frame, float l, float r)
        {
            int o = frame * _outChannels;
            _scratch[o] = l;
            _scratch[o + 1] = r;
        }

        public void Stop()
        {
            bool wasConnected = _isConnected;
            _isConnected = false;
            try { _output?.Stop(); } catch { }
            try { _output?.Dispose(); } catch { }
            _output = null;
            _buffer = null;
            if (wasConnected) OnLog?.Invoke("[DVS] Output stopped.");
        }

        public void Dispose() => Stop();
    }
}
