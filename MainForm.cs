using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace QwenImageEditClient
{
    public sealed class MainForm : Form
    {
        private readonly TextBox urlBox = new TextBox { Text = "http://127.0.0.1:8188", Dock = DockStyle.Fill };
        private readonly TextBox imageBox = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
        private readonly TextBox outputBox = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox backgroundBox = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical,
            Text = "a romantic wedding reception at night with warm fairy lights, flowers and elegant decorations"
        };
        private readonly NumericUpDown stepsBox = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 16, Width = 74 };
        private readonly NumericUpDown resolutionBox = new NumericUpDown { Minimum = 256, Maximum = 4096, Increment = 64, Value = 1024, Width = 84 };
        private readonly PictureBox inputPreview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
        private readonly PictureBox outputPreview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
        private readonly Button runButton = new Button { Text = "Thay background", Width = 150, Height = 34 };
        private readonly Button cancelButton = new Button { Text = "Hủy chờ", Width = 100, Height = 34, Enabled = false };
        private readonly Button openButton = new Button { Text = "Mở ảnh kết quả", Width = 130, Height = 34, Enabled = false };
        private readonly Label status = new Label { AutoSize = false, Dock = DockStyle.Fill, Text = "Sẵn sàng", AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, Visible = false };
        private CancellationTokenSource cancellation;
        private string latestOutput;

        public MainForm()
        {
            Text = "Qwen Image Edit - Thay background";
            MinimumSize = new Size(820, 650);
            Size = new Size(1050, 760);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);
            outputBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "QwenImageEdit");

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 3, RowCount = 7 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 85));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            Controls.Add(layout);

            AddRow(layout, 0, "ComfyUI URL", urlBox, null);
            var chooseInput = new Button { Text = "Chọn ảnh...", Dock = DockStyle.Fill };
            chooseInput.Click += ChooseInput;
            AddRow(layout, 1, "Ảnh đầu vào", imageBox, chooseInput);
            var chooseOutput = new Button { Text = "Chọn thư mục...", Dock = DockStyle.Fill };
            chooseOutput.Click += ChooseOutput;
            AddRow(layout, 2, "Thư mục kết quả", outputBox, chooseOutput);
            AddRow(layout, 3, "Mô tả nền mới", backgroundBox, null);

            var settings = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            settings.Controls.Add(new Label { Text = "Resolution", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
            settings.Controls.Add(resolutionBox);
            settings.Controls.Add(new Label { Text = "Steps", AutoSize = true, Padding = new Padding(12, 8, 0, 0) });
            settings.Controls.Add(stepsBox);
            settings.Controls.Add(runButton);
            settings.Controls.Add(cancelButton);
            settings.Controls.Add(openButton);
            layout.Controls.Add(settings, 1, 4);
            layout.SetColumnSpan(settings, 2);
            runButton.Click += RunClicked;
            cancelButton.Click += (s, e) => cancellation?.Cancel();
            openButton.Click += (s, e) => { if (File.Exists(latestOutput)) Process.Start(latestOutput); };

            var previews = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            previews.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            previews.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            previews.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            previews.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            previews.Controls.Add(new Label { Text = "Ảnh gốc", Dock = DockStyle.Fill }, 0, 0);
            previews.Controls.Add(new Label { Text = "Ảnh đã thay nền", Dock = DockStyle.Fill }, 1, 0);
            previews.Controls.Add(inputPreview, 0, 1);
            previews.Controls.Add(outputPreview, 1, 1);
            layout.Controls.Add(previews, 0, 5);
            layout.SetColumnSpan(previews, 3);
            layout.Controls.Add(status, 0, 6);
            layout.SetColumnSpan(status, 2);
            layout.Controls.Add(progress, 2, 6);
        }

        private static void AddRow(TableLayoutPanel layout, int row, string label, Control control, Control action)
        {
            layout.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
            layout.Controls.Add(control, 1, row);
            if (action == null) layout.SetColumnSpan(control, 2);
            else layout.Controls.Add(action, 2, row);
        }

        private void ChooseInput(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog { Title = "Chọn ảnh cần thay nền", Filter = "Ảnh|*.png;*.jpg;*.jpeg;*.webp;*.bmp|Tất cả|*.*" })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    imageBox.Text = dialog.FileName;
                    ShowPreview(inputPreview, dialog.FileName);
                }
        }

        private void ChooseOutput(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog { Description = "Chọn thư mục lưu ảnh kết quả", SelectedPath = Directory.Exists(outputBox.Text) ? outputBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) })
                if (dialog.ShowDialog(this) == DialogResult.OK) outputBox.Text = dialog.SelectedPath;
        }

        private static void ShowPreview(PictureBox box, string path)
        {
            Image next;
            // Clone để không khóa file ảnh đang được hiển thị.
            using (var file = File.OpenRead(path))
            using (var loaded = Image.FromStream(file)) next = new Bitmap(loaded);
            Image previous = box.Image;
            box.Image = next;
            previous?.Dispose();
        }

        private async void RunClicked(object sender, EventArgs e)
        {
            if (!File.Exists(imageBox.Text)) { MessageBox.Show(this, "Hãy chọn ảnh đầu vào hợp lệ."); return; }
            if (string.IsNullOrWhiteSpace(backgroundBox.Text)) { MessageBox.Show(this, "Hãy nhập mô tả nền mới."); return; }
            if (string.IsNullOrWhiteSpace(outputBox.Text)) { MessageBox.Show(this, "Hãy chọn thư mục kết quả."); return; }

            string workflowPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Workflows", "background_replace.json");
            var options = new ReplaceOptions
            {
                ImagePath = imageBox.Text, OutputFolder = outputBox.Text, Background = backgroundBox.Text,
                WorkflowPath = workflowPath, Steps = (int)stepsBox.Value, Resolution = (int)resolutionBox.Value
            };
            string url = urlBox.Text.Trim();
            runButton.Enabled = false;
            cancelButton.Enabled = true;
            openButton.Enabled = false;
            progress.Visible = true;
            cancellation = new CancellationTokenSource();
            try
            {
                using (var client = new ComfyUiClient(url))
                {
                    var reporter = new Progress<string>(message => status.Text = message);
                    var result = await new BackgroundReplaceService(client).RunAsync(options, reporter, cancellation.Token);
                    latestOutput = result.SavedPath;
                    ShowPreview(outputPreview, result.SavedPath);
                    openButton.Enabled = true;
                    status.Text = "Hoàn tất: " + result.SavedPath;
                    MessageBox.Show(this, "Đã lưu ảnh:\n" + result.SavedPath, "Hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (OperationCanceledException)
            {
                status.Text = "Đã hủy chờ. Nếu đã gửi prompt, ComfyUI có thể vẫn tiếp tục xử lý.";
            }
            catch (Exception ex)
            {
                status.Text = "Lỗi: " + ex.Message;
                MessageBox.Show(this, ex.Message, "Không thể thay nền", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                cancellation.Dispose();
                cancellation = null;
                progress.Visible = false;
                cancelButton.Enabled = false;
                runButton.Enabled = true;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (cancellation != null)
            {
                cancellation.Cancel();
                e.Cancel = true;
                status.Text = "Đang hủy; hãy đóng cửa sổ sau khi tác vụ dừng.";
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { inputPreview.Image?.Dispose(); outputPreview.Image?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
