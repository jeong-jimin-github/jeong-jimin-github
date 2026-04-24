using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ImageMosaicEditor
{
    public partial class MainForm : Form
    {
        // Currently loaded image and the path to its file
        private Bitmap? _originalBitmap;
        private string? _currentFilePath;

        // Variables for drag-selection of the mosaic region
        private bool _isDragging;
        private Point _dragStart;
        private Point _dragEnd;
        private Rectangle _selectionRect;

        // Mosaic block size (pixels)
        private const int MosaicBlockSize = 15;

        public MainForm()
        {
            InitializeComponent();
        }

        // ── File > Open ──────────────────────────────────────────────────────────
        private void MenuOpen_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title = "이미지 파일 열기",
                Filter = "이미지 파일 (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|모든 파일 (*.*)|*.*"
            };

            if (dlg.ShowDialog() != DialogResult.OK) return;

            LoadImage(dlg.FileName);
        }

        // ── File > Save ──────────────────────────────────────────────────────────
        private void MenuSave_Click(object sender, EventArgs e)
        {
            if (_originalBitmap == null || string.IsNullOrEmpty(_currentFilePath))
            {
                MessageBox.Show("저장할 이미지가 없습니다.", "알림",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SaveImage();
        }

        // ── Load helper ──────────────────────────────────────────────────────────
        private void LoadImage(string path)
        {
            // Dispose of any previous bitmap before loading a new one
            _originalBitmap?.Dispose();

            // Load into memory immediately so the file handle is released
            using var tmp = new Bitmap(path);
            _originalBitmap = new Bitmap(tmp);

            _currentFilePath = path;
            _selectionRect = Rectangle.Empty;

            pictureBox.Image = _originalBitmap;
            pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            this.Text = $"이미지 모자이크 편집기 - {System.IO.Path.GetFileName(path)}";
        }

        // ── Save helper ──────────────────────────────────────────────────────────
        private void SaveImage()
        {
            if (_originalBitmap == null || string.IsNullOrEmpty(_currentFilePath)) return;

            try
            {
                string ext = System.IO.Path.GetExtension(_currentFilePath).ToLowerInvariant();
                ImageFormat format = ext switch
                {
                    ".jpg" or ".jpeg" => ImageFormat.Jpeg,
                    _ => ImageFormat.Png
                };

                _originalBitmap.Save(_currentFilePath, format);
                MessageBox.Show("저장되었습니다.", "저장 완료",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"저장 중 오류가 발생했습니다:\n{ex.Message}", "오류",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── Mouse events for drag-select ─────────────────────────────────────────
        private void PictureBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (_originalBitmap == null) return;
            _isDragging = true;
            _dragStart = e.Location;
            _dragEnd = e.Location;
            _selectionRect = Rectangle.Empty;
        }

        private void PictureBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            _dragEnd = e.Location;
            UpdateSelectionRect();
            pictureBox.Invalidate(); // Trigger Paint to draw the selection rectangle
        }

        private void PictureBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;
            _dragEnd = e.Location;
            UpdateSelectionRect();

            if (_selectionRect.Width > 0 && _selectionRect.Height > 0)
            {
                ApplyMosaicToSelection();
            }
        }

        // ── Paint: draw the dotted selection rectangle while dragging ─────────────
        private void PictureBox_Paint(object sender, PaintEventArgs e)
        {
            if (_isDragging && _selectionRect.Width > 0 && _selectionRect.Height > 0)
            {
                using var pen = new Pen(Color.Red, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                e.Graphics.DrawRectangle(pen, _selectionRect);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────
        private void UpdateSelectionRect()
        {
            int x = Math.Min(_dragStart.X, _dragEnd.X);
            int y = Math.Min(_dragStart.Y, _dragEnd.Y);
            int w = Math.Abs(_dragEnd.X - _dragStart.X);
            int h = Math.Abs(_dragEnd.Y - _dragStart.Y);
            _selectionRect = new Rectangle(x, y, w, h);
        }

        /// <summary>
        /// Converts a point on the PictureBox (Zoom mode) to the corresponding
        /// pixel coordinate in the original bitmap.
        /// </summary>
        private Point PictureBoxToImage(Point pt)
        {
            if (_originalBitmap == null) return pt;

            RectangleF imgRect = GetImageRect();
            float scaleX = _originalBitmap.Width / imgRect.Width;
            float scaleY = _originalBitmap.Height / imgRect.Height;

            int ix = (int)((pt.X - imgRect.X) * scaleX);
            int iy = (int)((pt.Y - imgRect.Y) * scaleY);

            ix = Math.Clamp(ix, 0, _originalBitmap.Width - 1);
            iy = Math.Clamp(iy, 0, _originalBitmap.Height - 1);
            return new Point(ix, iy);
        }

        /// <summary>
        /// Returns the rectangle (in PictureBox client coordinates) where the
        /// image is actually drawn when SizeMode = Zoom.
        /// </summary>
        private RectangleF GetImageRect()
        {
            if (_originalBitmap == null) return RectangleF.Empty;

            float pbW = pictureBox.ClientSize.Width;
            float pbH = pictureBox.ClientSize.Height;
            float imgW = _originalBitmap.Width;
            float imgH = _originalBitmap.Height;

            float scale = Math.Min(pbW / imgW, pbH / imgH);
            float drawW = imgW * scale;
            float drawH = imgH * scale;
            float drawX = (pbW - drawW) / 2f;
            float drawY = (pbH - drawH) / 2f;
            return new RectangleF(drawX, drawY, drawW, drawH);
        }

        private void ApplyMosaicToSelection()
        {
            if (_originalBitmap == null) return;

            // Convert PictureBox coordinates → image coordinates
            Point topLeft = PictureBoxToImage(new Point(_selectionRect.Left, _selectionRect.Top));
            Point bottomRight = PictureBoxToImage(new Point(_selectionRect.Right, _selectionRect.Bottom));

            Rectangle imgRegion = new Rectangle(
                topLeft.X,
                topLeft.Y,
                bottomRight.X - topLeft.X,
                bottomRight.Y - topLeft.Y);

            if (imgRegion.Width <= 0 || imgRegion.Height <= 0) return;

            ApplyMosaic(_originalBitmap, imgRegion, MosaicBlockSize);

            // Refresh the PictureBox
            pictureBox.Image = _originalBitmap;
            pictureBox.Invalidate();
        }

        /// <summary>
        /// Applies a pixelation (mosaic) effect to the given region of a bitmap.
        /// Each block of <paramref name="blockSize"/> × <paramref name="blockSize"/>
        /// pixels is replaced by the average color of that block.
        /// Uses LockBits for direct memory access instead of GetPixel/SetPixel
        /// for significantly better performance on large images.
        /// </summary>
        private static unsafe void ApplyMosaic(Bitmap bmp, Rectangle region, int blockSize)
        {
            int right  = Math.Min(region.Right,  bmp.Width);
            int bottom = Math.Min(region.Bottom, bmp.Height);

            BitmapData data = bmp.LockBits(
                new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadWrite,
                PixelFormat.Format32bppArgb);

            try
            {
                int stride = data.Stride;
                byte* ptr  = (byte*)data.Scan0;

                for (int y = region.Top; y < bottom; y += blockSize)
                {
                    for (int x = region.Left; x < right; x += blockSize)
                    {
                        int bw = Math.Min(blockSize, right  - x);
                        int bh = Math.Min(blockSize, bottom - y);

                        // Compute the average color of this block
                        long r = 0, g = 0, b = 0, a = 0;
                        int count = bw * bh;

                        for (int py = y; py < y + bh; py++)
                        {
                            byte* row = ptr + py * stride + x * 4;
                            for (int px = 0; px < bw; px++, row += 4)
                            {
                                b += row[0];
                                g += row[1];
                                r += row[2];
                                a += row[3];
                            }
                        }

                        byte avgB = (byte)(b / count);
                        byte avgG = (byte)(g / count);
                        byte avgR = (byte)(r / count);
                        byte avgA = (byte)(a / count);

                        // Fill the block with the average color
                        for (int py = y; py < y + bh; py++)
                        {
                            byte* row = ptr + py * stride + x * 4;
                            for (int px = 0; px < bw; px++, row += 4)
                            {
                                row[0] = avgB;
                                row[1] = avgG;
                                row[2] = avgR;
                                row[3] = avgA;
                            }
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }
    }
}
