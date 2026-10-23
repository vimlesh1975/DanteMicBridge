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
        private readonly AudioCaptureManager _audioManager;
        private List<MMDevice> _captureDevices = new List<MMDevice>();

        public MainWindow()
        {
            InitializeComponent();

            _danteTransmitter = new DanteDalTransmitter();
            _danteTransmitter.OnLog += LogMessage;

            _audioManager = new AudioCaptureManager(_danteTransmitter);
            _audioManager.InputGain = 2.0f; // Default healthy preamp boost
            _audioManager.OnLog += LogMessage;
            _audioManager.OnAudioLevelChanged += UpdateLevels;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            CheckDalSdkStatus();
            LoadAudioDevices();
            LogMessage("[System] Application initialized ready for Dante microphone streaming.");
        }

        private void CheckDalSdkStatus()
        {
            if (_danteTransmitter.IsSdkAvailable)
            {
                TxtSdkStatus.Text = "DAL SDK: Native dal.dll Active";
                TxtSdkStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                TxtSdkExplanation.Text = "Audinate Dante Application Library (dal.dll) detected and active! Microphone audio is being marshalled into the native Dante driver.";
            }
            else
            {
                TxtSdkStatus.Text = "DAL SDK: Readiness & Simulation Mode";
                TxtSdkStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 204, 0));
                TxtSdkExplanation.Text = "Ready for official Audinate SDK: Place dal.dll in the application folder to activate native Dante network multicast.";
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
                    // Automatically auto-select USB audio microphone if present
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
            string channel = string.IsNullOrWhiteSpace(TxtTxChannel.Text) ? "Mic-01" : TxtTxChannel.Text.Trim();

            int sampleRate = 48000;
            if (CmbSampleRate.SelectedIndex == 1) sampleRate = 96000;
            if (CmbSampleRate.SelectedIndex == 2) sampleRate = 44100;

            bool success = _audioManager.StartCapture(selectedDevice, sampleRate);
            if (success)
            {
                BtnStart.IsEnabled = false;
                BtnStop.IsEnabled = true;
                CmbDevices.IsEnabled = false;
                CmbSampleRate.IsEnabled = false;
                CmbBitDepth.IsEnabled = false;

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