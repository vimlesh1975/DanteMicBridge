using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using NAudio.CoreAudioApi;
using DanteMicBridge.Core;

namespace DanteMicBridge
{
    public partial class MainWindow : Window
    {
        private readonly DanteDalTransmitter _danteTransmitter;
        private readonly Aes67AudioTransmitter _aes67Transmitter;
        private readonly DvsAudioTransmitter _dvsTransmitter;
        private readonly VbanAudioTransmitter _vbanTransmitter;
        private List<MMDevice> _dvsDevices = new List<MMDevice>();
        private IDanteAudioTransmitter _activeTransmitter;
        private readonly AudioCaptureManager _audioManager;
        private List<MMDevice> _captureDevices = new List<MMDevice>();

        public MainWindow()
        {
            _vbanTransmitter = new VbanAudioTransmitter();
            _vbanTransmitter.OnLog += LogMessage;

            _aes67Transmitter = new Aes67AudioTransmitter();
            _aes67Transmitter.OnLog += LogMessage;

            _dvsTransmitter = new DvsAudioTransmitter();
            _dvsTransmitter.OnLog += LogMessage;

            _danteTransmitter = new DanteDalTransmitter();
            _danteTransmitter.OnLog += LogMessage;

            // Default to VBAN (100% Free PC-to-PC Audio over LAN)
            _activeTransmitter = _vbanTransmitter;

            _audioManager = new AudioCaptureManager(_activeTransmitter);
            _audioManager.InputGain = 2.0f; // Default healthy preamp boost
            _audioManager.OnLog += LogMessage;
            _audioManager.OnAudioLevelChanged += UpdateLevels;

            InitializeComponent();

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateTransmitterModeUI();
            LoadAudioDevices();
            LogMessage("[System] Application initialized in VBAN LAN Direct mode (100% Free PC-to-PC).");
        }

        private void CmbProtocolMode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CmbProtocolMode == null || _vbanTransmitter == null || _audioManager == null) return;
            UpdateTransmitterModeUI();
        }

        private void UpdateTransmitterModeUI()
        {
            if (CmbProtocolMode == null || _audioManager == null || _vbanTransmitter == null || _aes67Transmitter == null || _danteTransmitter == null || _dvsTransmitter == null) return;

            if (PanelVbanParams != null) PanelVbanParams.Visibility = CmbProtocolMode.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (PanelDvsParams != null) PanelDvsParams.Visibility = CmbProtocolMode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (GridAes67Params != null) GridAes67Params.Visibility = CmbProtocolMode.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;

            if (CmbProtocolMode.SelectedIndex == 0) // VBAN Direct LAN
            {
                _activeTransmitter = _vbanTransmitter;
                _audioManager.SetTransmitter(_activeTransmitter);
                if (TxtSdkStatus != null)
                {
                    TxtSdkStatus.Text = "VBAN Direct: 100% Free (No License Required)";
                    TxtSdkStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                }
                if (TxtSdkExplanation != null)
                {
                    TxtSdkExplanation.Text = "Microphone broadcasts over your local Wi-Fi/LAN directly to PC 2. On PC 2, open free VBAN Receptor to hear your voice immediately through PC 2's speakers!";
                }
                return;
            }

            if (CmbProtocolMode.SelectedIndex == 1) // Dante Virtual Soundcard
            {
                _activeTransmitter = _dvsTransmitter;
                _audioManager.SetTransmitter(_activeTransmitter);
                LoadDvsDevices();
                if (TxtSdkStatus != null)
                {
                    TxtSdkStatus.Text = "DVS Mode: Appears in Dante Controller";
                    TxtSdkStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 163, 255));
                }
                if (TxtSdkExplanation != null)
                {
                    TxtSdkExplanation.Text = "Requires Dante Virtual Soundcard installed and started. This PC shows up in Dante Controller; route Tx channels 1/2 to any receiver.";
                }
                return;
            }

            if (CmbProtocolMode.SelectedIndex == 2) // AES67
            {
                _activeTransmitter = _aes67Transmitter;
                _audioManager.SetTransmitter(_activeTransmitter);
                if (TxtSdkStatus != null)
                {
                    TxtSdkStatus.Text = "AES67 Mode: Open Multicast Standard";
                    TxtSdkStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                }
                if (TxtSdkExplanation != null)
                {
                    TxtSdkExplanation.Text = "AES67 RTP Multicast active. Supported by hardware Dante devices with AES67 mode enabled in Dante Controller.";
                }
                return;
            }

            // DAL SDK
            _activeTransmitter = _danteTransmitter;
            _audioManager.SetTransmitter(_activeTransmitter);
            if (TxtSdkStatus != null)
            {
                if (_danteTransmitter.IsSdkAvailable)
                {
                    TxtSdkStatus.Text = "DAL SDK: Native dal.dll Active";
                    TxtSdkStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                    TxtSdkExplanation.Text = "Audinate Dante Application Library (dal.dll) detected and active!";
                }
                else
                {
                    TxtSdkStatus.Text = "DAL SDK: License / dal.dll Missing";
                    TxtSdkStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 204, 0));
                    TxtSdkExplanation.Text = "Audinate requires a commercial OEM license and dal.dll. Switch to VBAN for free, permanent PC-to-PC audio.";
                }
            }
        }

        private void LoadDvsDevices()
        {
            CmbDvsDevice.Items.Clear();
            try
            {
                _dvsDevices = DvsAudioTransmitter.GetOutputDevices();
                foreach (var d in _dvsDevices) CmbDvsDevice.Items.Add(d.FriendlyName);
                if (CmbDvsDevice.Items.Count > 0) CmbDvsDevice.SelectedIndex = 0;
                bool hasDante = _dvsDevices.Count > 0 && _dvsDevices[0].FriendlyName.Contains("Dante", StringComparison.OrdinalIgnoreCase);
                if (!hasDante) LogMessage("[DVS] No 'Dante' output device found. Install/start Dante Virtual Soundcard, then press Refresh Devices.");
            }
            catch (Exception ex)
            {
                LogMessage($"[DVS Error] Failed to list output devices: {ex.Message}");
            }
        }

        private void LoadAudioDevices()
        {
            CmbDevices.Items.Clear();
            try
            {
                _captureDevices = AudioCaptureManager.GetInputDevices();
                int selectedIndex = 0;
                for (int i = 0; i < _captureDevices.Count; i++)
                {
                    var dev = _captureDevices[i];
                    CmbDevices.Items.Add(dev.FriendlyName);
                    if (dev.FriendlyName.Contains("USBAudio") || dev.FriendlyName.Contains("USB"))
                    {
                        selectedIndex = i;
                    }
                }

                if (CmbDevices.Items.Count > 0)
                {
                    CmbDevices.SelectedIndex = selectedIndex;
                }
                else
                {
                    CmbDevices.Items.Add("No audio capture device found");
                    CmbDevices.SelectedIndex = 0;
                    BtnStart.IsEnabled = false;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"[Device Error] Failed to enumerate audio devices: {ex.Message}");
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadAudioDevices();
            if (CmbProtocolMode.SelectedIndex == 1) LoadDvsDevices();
            LogMessage("[System] Audio input devices reloaded.");
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (CmbDevices.SelectedIndex < 0 || CmbDevices.SelectedIndex >= _captureDevices.Count)
            {
                MessageBox.Show("Please select a valid microphone input device.", "Input Device Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedDevice = _captureDevices[CmbDevices.SelectedIndex];
            string channel = string.IsNullOrWhiteSpace(TxtTxChannel.Text) ? "Mic-In" : TxtTxChannel.Text.Trim();

            int sampleRate = 48000;
            if (CmbSampleRate.SelectedIndex == 1) sampleRate = 96000;
            if (CmbSampleRate.SelectedIndex == 2) sampleRate = 44100;

            Func<int, bool> initTx = captureRate =>
            {
                if (_activeTransmitter is VbanAudioTransmitter vban)
                {
                    string targetIp = string.IsNullOrWhiteSpace(TxtVbanIp.Text) ? "255.255.255.255" : TxtVbanIp.Text.Trim();
                    int port = int.TryParse(TxtVbanPort.Text, out int p) ? p : 6980;
                    return vban.Initialize(channel, captureRate, 16, targetIp, port);
                }
                if (_activeTransmitter is Aes67AudioTransmitter aesTransmitter)
                {
                    string mcastIp = string.IsNullOrWhiteSpace(TxtMulticastIp.Text) ? "239.69.1.1" : TxtMulticastIp.Text.Trim();
                    int port = int.TryParse(TxtRtpPort.Text, out int p) ? p : 5004;
                    return aesTransmitter.Initialize(channel, captureRate, 24, mcastIp, port);
                }
                if (_activeTransmitter is DvsAudioTransmitter dvs)
                {
                    if (CmbDvsDevice.SelectedIndex < 0 || CmbDvsDevice.SelectedIndex >= _dvsDevices.Count)
                    {
                        LogMessage("[DVS] Select a Dante Virtual Soundcard output device first.");
                        return false;
                    }
                    return dvs.Initialize(channel, captureRate, 24, _dvsDevices[CmbDvsDevice.SelectedIndex]);
                }
                return _activeTransmitter.Initialize(channel, captureRate, 24);
            };

            bool success = _audioManager.StartCapture(selectedDevice, sampleRate, initTx);
            if (success)
            {
                BtnStart.IsEnabled = false;
                BtnStop.IsEnabled = true;
                CmbDevices.IsEnabled = false;
                CmbSampleRate.IsEnabled = false;
                CmbBitDepth.IsEnabled = false;
                CmbProtocolMode.IsEnabled = false;

                TxtStatus.Text = "STREAMING (LIVE)";
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                BadgeDanteStatus.Background = new SolidColorBrush(Color.FromArgb(40, 0, 230, 118));
            }
            else
            {
                MessageBox.Show("Failed to open audio input device with WASAPI.", "Stream Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _audioManager.StopCapture();

            BtnStart.IsEnabled = true;
            BtnStop.IsEnabled = false;
            CmbDevices.IsEnabled = true;
            CmbSampleRate.IsEnabled = true;
            CmbBitDepth.IsEnabled = true;
            CmbProtocolMode.IsEnabled = true;

            TxtStatus.Text = "STANDBY";
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(120, 144, 156));
            BadgeDanteStatus.Background = new SolidColorBrush(Color.FromRgb(30, 35, 46));
            PbLevelL.Value = 0;
            PbLevelR.Value = 0;
            TxtDbReadout.Text = "-inf dB";
        }

        private void SliderGain_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_audioManager != null)
            {
                _audioManager.InputGain = (float)e.NewValue;
            }
            if (TxtGainVal != null)
            {
                float gain = (float)e.NewValue;
                double db = gain > 0 ? 20 * Math.Log10(gain) : -60;
                TxtGainVal.Text = $"{gain:0.00}x ({db:+0.0;-0.0;0.0} dB)";
            }
        }

        private void UpdateLevels(float levelL, float levelR)
        {
            Dispatcher.InvokeAsync(() =>
            {
                // Dynamic scaling so even normal speech (~-30dB to -18dB) registers clearly on the meter
                // Standard audio logarithmic/root perceptual curve
                float visualL = MathF.Pow(Math.Clamp(levelL, 0f, 1f), 0.5f) * 100f;
                float visualR = MathF.Pow(Math.Clamp(levelR, 0f, 1f), 0.5f) * 100f;

                PbLevelL.Value = visualL;
                PbLevelR.Value = visualR;

                float maxPeak = Math.Max(levelL, levelR);
                if (maxPeak > 0.0001f)
                {
                    double db = 20 * Math.Log10(maxPeak);
                    TxtDbReadout.Text = $"{db:0.0} dB";
                }
                else
                {
                    TxtDbReadout.Text = "-inf dB";
                }
            });
        }

        private void LogMessage(string message)
        {
            Dispatcher.InvokeAsync(() =>
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                TxtLogs.AppendText($"[{timestamp}] {message}\n");
                LogScrollViewer.ScrollToEnd();
            });
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _audioManager.Dispose();
        }
    }
}