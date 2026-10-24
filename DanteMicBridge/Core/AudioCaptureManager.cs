using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DanteMicBridge.Core
{
    public class AudioCaptureManager : IDisposable
    {
        private WasapiCapture? _capture;
        private IDanteAudioTransmitter _danteTransmitter;
        private bool _isCapturing;
        private float _currentPeakL;
        private float _currentPeakR;
        private float _inputGain = 1.0f;

        public bool IsCapturing => _isCapturing;
        public float CurrentPeakL => _currentPeakL;
        public float CurrentPeakR => _currentPeakR;
        public float InputGain
        {
            get => _inputGain;
            set => _inputGain = Math.Clamp(value, 0.0f, 10.0f);
        }

        public event Action<float, float>? OnAudioLevelChanged;
        public event Action<string>? OnLog;

        public AudioCaptureManager(IDanteAudioTransmitter transmitter)
        {
            _danteTransmitter = transmitter;
        }

        public void SetTransmitter(IDanteAudioTransmitter transmitter)
        {
            _danteTransmitter = transmitter;
        }

        public static List<MMDevice> GetInputDevices()
        {
            var devices = new List<MMDevice>();
            var enumerator = new MMDeviceEnumerator();
            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var device in endpoints)
            {
                devices.Add(device);
            }
            return devices;
        }

        public bool StartCapture(MMDevice device, int requestedSampleRate = 48000, Func<int, bool>? initTransmitter = null)
        {
            StopCapture();

            try
            {
                OnLog?.Invoke($"[WASAPI] Initializing capture on: {device.FriendlyName}");
                
                // Use default endpoint format if available
                _capture = new WasapiCapture(device, true, 20); // 20ms buffer for USB reliability
                var wf = _capture.WaveFormat;
                
                OnLog?.Invoke($"[WASAPI Format] Rate={wf.SampleRate}Hz, Bits={wf.BitsPerSample}, Channels={wf.Channels}, Encoding={wf.Encoding}, ExtensibleSubFormat={(wf is WaveFormatExtensible wfe ? wfe.SubFormat.ToString() : "N/A")}");

                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += (s, a) =>
                {
                    _isCapturing = false;
                    if (a.Exception != null)
                    {
                        OnLog?.Invoke($"[WASAPI Stopped With Error] {a.Exception.Message}");
                    }
                    else
                    {
                        OnLog?.Invoke("[WASAPI] Capture stopped.");
                    }
                };

                bool txOk = initTransmitter != null
                    ? initTransmitter(wf.SampleRate)
                    : _danteTransmitter.Initialize("Mic-In", wf.SampleRate, 24);
                if (!txOk)
                {
                    OnLog?.Invoke("[Transmitter] Failed to initialize output; capture not started.");
                    _capture.Dispose();
                    _capture = null;
                    return false;
                }
                _capture.StartRecording();
                _isCapturing = true;
                OnLog?.Invoke($"[WASAPI] Streaming active and capturing.");
                return true;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[WASAPI Error] Failed to start capture: {ex.Message}");
                return false;
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_capture == null || e.BytesRecorded == 0) return;

            var waveFormat = _capture.WaveFormat;
            int channels = Math.Max(1, waveFormat.Channels);
            int bitsPerSample = waveFormat.BitsPerSample;
            int bytesPerSample = bitsPerSample / 8;

            if (bytesPerSample <= 0) return;

            int totalSamples = e.BytesRecorded / bytesPerSample;
            int sampleCount = totalSamples / channels;

            if (sampleCount <= 0) return;

            float[] samplesL = new float[sampleCount];
            float[]? samplesR = channels > 1 ? new float[sampleCount] : null;

            int byteIndex = 0;
            float maxL = 0f;
            float maxR = 0f;

            // Universal sample decoder covering: 32-bit Float, 16-bit PCM, 24-bit PCM, 32-bit PCM Extensible
            for (int i = 0; i < sampleCount; i++)
            {
                float left = 0f;
                float right = 0f;

                if (bitsPerSample == 32 && waveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                {
                    left = BitConverter.ToSingle(e.Buffer, byteIndex);
                    byteIndex += 4;
                    if (channels > 1)
                    {
                        right = BitConverter.ToSingle(e.Buffer, byteIndex);
                        byteIndex += 4;
                        for (int ch = 2; ch < channels; ch++) byteIndex += 4;
                    }
                }
                else if (bitsPerSample == 16)
                {
                    short sL = BitConverter.ToInt16(e.Buffer, byteIndex);
                    left = sL / 32768.0f;
                    byteIndex += 2;
                    if (channels > 1)
                    {
                        short sR = BitConverter.ToInt16(e.Buffer, byteIndex);
                        right = sR / 32768.0f;
                        byteIndex += 2;
                        for (int ch = 2; ch < channels; ch++) byteIndex += 2;
                    }
                }
                else if (bitsPerSample == 24)
                {
                    // 24-bit packed PCM Little-Endian
                    int sL = (e.Buffer[byteIndex] | (e.Buffer[byteIndex + 1] << 8) | (e.Buffer[byteIndex + 2] << 16));
                    if ((sL & 0x800000) != 0) sL |= unchecked((int)0xFF000000); // Sign extend
                    left = sL / 8388608.0f;
                    byteIndex += 3;

                    if (channels > 1)
                    {
                        int sR = (e.Buffer[byteIndex] | (e.Buffer[byteIndex + 1] << 8) | (e.Buffer[byteIndex + 2] << 16));
                        if ((sR & 0x800000) != 0) sR |= unchecked((int)0xFF000000);
                        right = sR / 8388608.0f;
                        byteIndex += 3;
                        for (int ch = 2; ch < channels; ch++) byteIndex += 3;
                    }
                }
                else if (bitsPerSample == 32)
                {
                    // 32-bit PCM (e.g. 24-bit in 32-bit container or 32-bit int)
                    int sL = BitConverter.ToInt32(e.Buffer, byteIndex);
                    left = sL / 2147483648.0f;
                    byteIndex += 4;

                    if (channels > 1)
                    {
                        int sR = BitConverter.ToInt32(e.Buffer, byteIndex);
                        right = sR / 2147483648.0f;
                        byteIndex += 4;
                        for (int ch = 2; ch < channels; ch++) byteIndex += 4;
                    }
                }

                // Apply Software Preamp Gain
                left *= _inputGain;
                samplesL[i] = left;
                float absL = Math.Abs(left);
                if (absL > maxL) maxL = absL;

                if (channels > 1)
                {
                    right *= _inputGain;
                    if (samplesR != null) samplesR[i] = right;
                    float absR = Math.Abs(right);
                    if (absR > maxR) maxR = absR;
                }
                else
                {
                    maxR = maxL;
                }
            }

            _currentPeakL = maxL;
            _currentPeakR = maxR;

            OnAudioLevelChanged?.Invoke(maxL, maxR);
            _danteTransmitter.TransmitAudioSamples(samplesL, samplesR, sampleCount);
        }

        public void StopCapture()
        {
            if (_capture != null)
            {
                try
                {
                    _capture.StopRecording();
                    _capture.Dispose();
                }
                catch { }
                _capture = null;
            }
            _danteTransmitter.Stop();
            _isCapturing = false;
            _currentPeakL = 0;
            _currentPeakR = 0;
            OnAudioLevelChanged?.Invoke(0, 0);
        }

        public void Dispose()
        {
            StopCapture();
        }
    }
}
