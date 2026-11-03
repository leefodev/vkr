using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;
// BLE-директивы
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace BLE_Mic
{
    public partial class Form1 : Form
    {
        // === ПОЛЯ ДЛЯ ГРАФИКА ===
        private List<int> audioHistory = new List<int>();
        private const int MaxAudioPoints = 400;
        private const float ScaleY = 0.5f; // Увеличенный масштаб для наглядности

        private PictureBox audioBox;
        private ComboBox portComboBox;
        private Button connectButton;
        private Label statusLabel;
        private Label rawDataLabel; // Метка для вывода сырых данных

        // === ПОЛЯ ДЛЯ BLE ===
        private BluetoothLEDevice bleDevice = null;
        private GattCharacteristic dataCharacteristic = null;
        private string _bleBuffer = ""; // Буфер для сборки строк

        private Guid serviceUuid = Guid.Parse("4fafc201-1fb5-459e-8fcc-c5c9c331914b");
        private Guid charUuid = Guid.Parse("beb5483e-36e1-4688-b7f5-ea07361b26a8");

        public Form1()
        {
            this.DoubleBuffered = true;
            this.Text = "BLE Sound Visualizer";
            this.ClientSize = new Size(900, 450); // Сделал окно чуть шире для текста
            this.BackColor = Color.White;
            this.StartPosition = FormStartPosition.CenterScreen;

            // 1. Верхняя панель (управление)
            Panel topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = Color.LightGray
            };

            portComboBox = new ComboBox
            {
                Location = new Point(10, 8),
                Width = 100,
                Visible = false // Скрываем, так как для BLE не нужен
            };

            connectButton = new Button
            {
                Location = new Point(10, 7),
                Text = "Подключить (BLE)",
                Width = 130
            };
            connectButton.Click += ConnectButton_Click;

            statusLabel = new Label
            {
                Location = new Point(150, 12),
                Text = "Не подключено",
                AutoSize = true,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.DarkRed
            };

            // Метка для вывода сырых данных
            rawDataLabel = new Label
            {
                Location = new Point(300, 12),
                Text = "Данные: ",
                AutoSize = true,
                Font = new Font("Consolas", 10, FontStyle.Regular),
                ForeColor = Color.Black
            };

            topPanel.Controls.Add(portComboBox);
            topPanel.Controls.Add(connectButton);
            topPanel.Controls.Add(statusLabel);
            topPanel.Controls.Add(rawDataLabel);

            // 2. Область для графика
            audioBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black
            };
            audioBox.Paint += AudioBox_Paint;

            this.Controls.Add(audioBox);
            this.Controls.Add(topPanel);

            this.FormClosing += Form1_FormClosing;
        }

        // === ЛОГИКА BLE ===
        private async void ConnectButton_Click(object sender, EventArgs e)
        {
            if (bleDevice != null)
            {
                // Отключение
                if (dataCharacteristic != null)
                {
                    dataCharacteristic.ValueChanged -= DataCharacteristic_ValueChanged;
                    dataCharacteristic = null;
                }
                bleDevice.Dispose();
                bleDevice = null;
                statusLabel.Text = "Отключено";
                statusLabel.ForeColor = Color.DarkRed;
                connectButton.Text = "Подключить (BLE)";
                rawDataLabel.Text = "Данные: ";
                return;
            }

            connectButton.Enabled = false;
            statusLabel.Text = "Поиск устройства...";
            statusLabel.ForeColor = Color.Orange;

            try
            {
                await ConnectToBleDeviceAsync("SoundVisualizer");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка BLE: " + ex.Message);
                statusLabel.Text = "Ошибка";
                statusLabel.ForeColor = Color.DarkRed;
            }
            finally
            {
                connectButton.Enabled = true;
            }
        }

        private async Task ConnectToBleDeviceAsync(string deviceName)
        {
            var watcher = new BluetoothLEAdvertisementWatcher();
            watcher.AllowExtendedAdvertisements = true;
            watcher.ScanningMode = BluetoothLEScanningMode.Active;

            var tcs = new TaskCompletionSource<ulong>();

            watcher.Received += (w, args) =>
            {
                if (args.Advertisement.LocalName == deviceName)
                {
                    tcs.TrySetResult(args.BluetoothAddress);
                    watcher.Stop();
                }
            };

            watcher.Start();
            var timeoutTask = Task.Delay(10000);
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                watcher.Stop();
                throw new TimeoutException("Устройство не найдено. Проверьте, что ESP32 включена и не подключена к другому устройству.");
            }

            ulong address = await tcs.Task;

            // Дальше ваш существующий код подключения (BluetoothLEDevice.FromBluetoothAddressAsync и т.д.)
            statusLabel.Text = "Подключение...";
            bleDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            if (bleDevice == null) throw new Exception("Не удалось создать устройство.");

            await Task.Delay(500);

            var services = await bleDevice.GetGattServicesAsync();
            GattDeviceService targetService = null;
            foreach (var s in services.Services)
            {
                if (s.Uuid == serviceUuid) { targetService = s; break; }
            }
            if (targetService == null) throw new Exception("Сервис не найден.");

            var chars = await targetService.GetCharacteristicsAsync();
            foreach (var c in chars.Characteristics)
            {
                if (c.Uuid == charUuid) { dataCharacteristic = c; break; }
            }
            if (dataCharacteristic == null) throw new Exception("Характеристика не найдена.");

            var status = await dataCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);

            if (status == GattCommunicationStatus.Success)
            {
                dataCharacteristic.ValueChanged += DataCharacteristic_ValueChanged;
                statusLabel.Text = "Подключено (BLE)";
                statusLabel.ForeColor = Color.Green;
                connectButton.Text = "Отключить";
            }
            else
            {
                throw new Exception("Не удалось подписаться на уведомления. Статус: " + status);
            }
        }

        private void DataCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            byte[] bytes = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(bytes);
            string incoming = System.Text.Encoding.UTF8.GetString(bytes);

            _bleBuffer += incoming;

            int newlineIndex;
            while ((newlineIndex = _bleBuffer.IndexOfAny(new[] { '\n', '\r' })) >= 0)
            {
                string line = _bleBuffer.Substring(0, newlineIndex).Trim();
                _bleBuffer = _bleBuffer.Substring(newlineIndex + 1);

                if (!string.IsNullOrEmpty(line) && line.StartsWith("AUD:"))
                {
                    string numbersStr = line.Substring(4);

                    this.BeginInvoke((MethodInvoker)delegate
                    {
                        // Выводим сырые значения
                        rawDataLabel.Text = "Данные: " + numbersStr;

                        // Парсим для графика
                        string[] parts = numbersStr.Split(',');
                        foreach (var p in parts)
                        {
                            if (int.TryParse(p, out int val))
                                audioHistory.Add(val);
                        }
                        while (audioHistory.Count > MaxAudioPoints)
                            audioHistory.RemoveAt(0);
                        audioBox.Invalidate();
                    });
                }
            }
        }

        // === ОТРИСОВКА ГРАФИКА ===
        private void AudioBox_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int width = audioBox.Width;
            int height = audioBox.Height;
            int centerY = height / 2;

            // Центральная линия
            using (Pen axisPen = new Pen(Color.DarkGreen, 1))
            {
                g.DrawLine(axisPen, 0, centerY, width, centerY);
            }

            if (audioHistory.Count < 2) return;

            using (Pen audioPen = new Pen(Color.Lime, 2))
            {
                PointF[] points = new PointF[audioHistory.Count];
                float stepX = (float)width / (MaxAudioPoints - 1);

                for (int i = 0; i < audioHistory.Count; i++)
                {
                    float x = i * stepX;
                    float y = centerY - (audioHistory[i] * ScaleY);

                    if (y < 0) y = 0;
                    if (y > height) y = height;

                    points[i] = new PointF(x, y);
                }

                g.DrawLines(audioPen, points);
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (dataCharacteristic != null)
                dataCharacteristic.ValueChanged -= DataCharacteristic_ValueChanged;
            bleDevice?.Dispose();
        }
    }
}