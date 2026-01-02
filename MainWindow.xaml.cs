using Microsoft.Win32;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StsModAssist {
	public partial class MainWindow : Window {
		private Point _lastMousePosition;
		private bool _isDragging = false;
		private double _maskWidth, _maskHeight;
		private BitmapImage _rawMaskBitmap;

		// 偏移量
		private const double CanvasMargin = 200;

		public MainWindow() {
			InitializeComponent();
			MainCanvas.MouseDown += MainCanvas_MouseDown;
			MainCanvas.MouseMove += MainCanvas_MouseMove;
			MainCanvas.MouseUp += MainCanvas_MouseUp;
			MainCanvas.MouseWheel += MainCanvas_MouseWheel;
		}

		// 加载遮罩图片
		private void LoadMask_Click(object sender, RoutedEventArgs e) {
			OpenFileDialog openFileDialog = new OpenFileDialog { Filter = "PNG图片 (*.png)|*.png" };
			if(openFileDialog.ShowDialog() == true) {
				_rawMaskBitmap = new BitmapImage(new Uri(openFileDialog.FileName));
				_maskWidth = _rawMaskBitmap.PixelWidth;
				_maskHeight = _rawMaskBitmap.PixelHeight;

				// 设置画布尺寸
				MainCanvas.Width = (double)(int)(_maskWidth * 1.2 + CanvasMargin);
				MainCanvas.Height = (double)(int)(_maskHeight * 1.2 + CanvasMargin);

				// 显示辅助遮罩图片
				//MaskOverlayImage.Source = _rawMaskBitmap;
				MaskOverlayImage.Source = CreateInverseAlphaMask(_rawMaskBitmap);
				Canvas.SetLeft(MaskOverlayImage, 0);
				Canvas.SetTop(MaskOverlayImage, 0);

				// 设置红框
				ExportGuide.Width = _maskWidth;
				ExportGuide.Height = _maskHeight;
				Canvas.SetLeft(ExportGuide, (MainCanvas.Width- _maskWidth) / 2);
				Canvas.SetTop(ExportGuide, (MainCanvas.Height - _maskHeight) / 2);
				ExportGuide.Visibility = Visibility.Visible;

				ZoomSlider.Value = 0.5;
				this.Dispatcher.BeginInvoke(new Action(() => {
					// 获取 ScrollViewer 可视区域的大小（减去一些边距以留白）
					double availableWidth = MainScrollViewer.ActualWidth - 40;
					double availableHeight = MainScrollViewer.ActualHeight - 40;

					// 计算缩放比例
					double zoomX = availableWidth / MainCanvas.Width;
					double zoomY = availableHeight / MainCanvas.Height;
					double bestZoom = Math.Min(zoomX, zoomY);

					// 限制一下最小缩放，不要缩得太小
					ZoomSlider.Value = Math.Max(0.1, bestZoom);
				}), System.Windows.Threading.DispatcherPriority.Loaded);
			}
		}
		private BitmapSource CreateInverseAlphaMask(BitmapSource mask) {
			int targetWidth = (int)MainCanvas.Width;
			int targetHeight = (int)MainCanvas.Height;

			PixelFormat format = PixelFormats.Bgra32;
			int bytesPerPixel = format.BitsPerPixel / 8;
			int stride = targetWidth * bytesPerPixel;

			byte[] targetPixels = new byte[targetHeight * stride];

			byte gray = 240;
			byte backgroundAlpha = 180;

			for(int y = 0;y < targetHeight;y++) {
				for(int x = 0;x < targetWidth;x++) {
					int index = y * stride + x * bytesPerPixel;
					targetPixels[index + 0] = gray;             // B
					targetPixels[index + 1] = gray;             // G
					targetPixels[index + 2] = gray;             // R
					targetPixels[index + 3] = backgroundAlpha;  // A
				}
			}

			BitmapSource maskSource = mask.Format == PixelFormats.Bgra32
				? mask
				: new FormatConvertedBitmap(mask, PixelFormats.Bgra32, null, 0);

			int maskWidth = maskSource.PixelWidth;
			int maskHeight = maskSource.PixelHeight;
			int maskStride = maskWidth * bytesPerPixel;

			byte[] maskPixels = new byte[maskHeight * maskStride];
			maskSource.CopyPixels(maskPixels, maskStride, 0);

			int offsetX = (targetWidth - maskWidth) / 2;
			int offsetY = (targetHeight - maskHeight) / 2;
			// 计算遮罩
			for(int y = 0;y < maskHeight;y++) {
				for(int x = 0;x < maskWidth;x++) {
					int maskIndex = y * maskStride + x * bytesPerPixel;
					byte maskAlpha = maskPixels[maskIndex + 3];

					int targetX = offsetX + x;
					int targetY = offsetY + y;

					if((uint)targetX >= (uint)targetWidth ||
						(uint)targetY >= (uint)targetHeight)
						continue;

					int targetIndex = targetY * stride + targetX * bytesPerPixel;
					// 反向线性插值计算Alpha
					int newAlpha = backgroundAlpha * (255 - maskAlpha) / 255;
					targetPixels[targetIndex + 3] = (byte)newAlpha;
				}
			}

			return BitmapSource.Create(targetWidth,targetHeight,96,96,format,null,targetPixels,stride);
		}

		// 加载图片
		private void LoadImage_Click(object sender, RoutedEventArgs e) {
			if(_rawMaskBitmap == null) { MessageBox.Show("请先加载遮罩"); return; }
			OpenFileDialog openFileDialog = new OpenFileDialog { Filter = "图片|*.jpg;*.png;*.bmp" };
			if(openFileDialog.ShowDialog() == true) {
				BitmapImage img = new BitmapImage(new Uri(openFileDialog.FileName));
				TargetImage.Source = img;
				imgScale.ScaleX = 1; imgScale.ScaleY = 1;
				imgRotate.Angle = 0;
				imgTranslate.X = 0; imgTranslate.Y = 0;

				// 居中图片
				Canvas.SetLeft(TargetImage, (MainCanvas.Width - img.PixelWidth) / 2);
				Canvas.SetTop(TargetImage, (MainCanvas.Height - img.PixelHeight) / 2);
			}
		}

		// 鼠标按下监听，用于下面的拖拽时间
		private void MainCanvas_MouseDown(object sender, MouseButtonEventArgs e) {
			_isDragging = true;
			_lastMousePosition = e.GetPosition(this);
			MainCanvas.CaptureMouse();
		}

		// 鼠标移动监听，实现拖拽图片或者旋转图片
		private void MainCanvas_MouseMove(object sender, MouseEventArgs e) {
			if(!_isDragging || TargetImage.Source == null) return;
			Point currentPos = e.GetPosition(this);
			Vector delta = currentPos - _lastMousePosition;

			// 获取当前缩放比例，修正移动速度
			double zoom = ZoomSlider.Value;

			if(e.LeftButton == MouseButtonState.Pressed) {
				imgTranslate.X += delta.X / zoom;
				imgTranslate.Y += delta.Y / zoom;
			} else if(e.RightButton == MouseButtonState.Pressed) {
				imgRotate.Angle += delta.X;
			}
			_lastMousePosition = currentPos;
		}

		private void MainCanvas_MouseUp(object sender, MouseButtonEventArgs e) {
			_isDragging = false;
			MainCanvas.ReleaseMouseCapture();
		}

		private void MainCanvas_MouseWheel(object sender, MouseWheelEventArgs e) {
			// 按住ctrl，缩放图片
			if(Keyboard.Modifiers == ModifierKeys.Control) {
				double factor = e.Delta > 0 ? 1.1 : 0.9;
				imgScale.ScaleX *= factor;
				imgScale.ScaleY *= factor;
				e.Handled = true; // 拦截事件，防止 ScrollViewer 同时滚动

			// 按住Alt，缩放画布
			} else if(Keyboard.Modifiers == ModifierKeys.Alt) {
				double zoomStep = 1.1;
				if(e.Delta > 0) {
					ZoomSlider.Value = Math.Min(ZoomSlider.Maximum, ZoomSlider.Value * zoomStep);
				} else {
					ZoomSlider.Value = Math.Max(ZoomSlider.Minimum, ZoomSlider.Value / zoomStep);
				}
				e.Handled = true;
			}
		}

		//保存图片结果
		private void SaveResult_Click(object sender, RoutedEventArgs e) {
			if(TargetImage.Source == null || _rawMaskBitmap == null) {
				MessageBox.Show("请先加载遮罩和图片");
				return;
			}

			SaveFileDialog saveFileDialog = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = "裁剪结果.png" };
			if(saveFileDialog.ShowDialog() == true) {
				try {
					// 记录当前 UI 状态并隐藏辅助层
					MaskOverlayImage.Visibility = Visibility.Collapsed;
					ExportGuide.Visibility = Visibility.Collapsed;

					// 计算裁切起始点 (必须与 LoadMask_Click 中的居中逻辑完全一致)
					int cropX = (int)((MainCanvas.Width - _maskWidth) / 2);
					int cropY = (int)((MainCanvas.Height - _maskHeight) / 2);

					// 渲染整个图片层
					// 注意：这里渲染 ImageLayer，它包含了 TargetImage
					RenderTargetBitmap rtb = new RenderTargetBitmap(
						(int)MainCanvas.Width, (int)MainCanvas.Height, 96, 96, PixelFormats.Pbgra32);
					rtb.Render(ImageLayer);

					// 执行裁切
					CroppedBitmap croppedImage = new CroppedBitmap(rtb,
						new Int32Rect(cropX, cropY, (int)_maskWidth, (int)_maskHeight));

					// 像素级合成：将裁切出的图片与原始遮罩的 Alpha 通道合并
					WriteableBitmap finalResult = ApplyMaskToImage(croppedImage, _rawMaskBitmap);
					string selectedPath = saveFileDialog.FileName;

					if(ExSaveBox.IsChecked == true) {
						// 计算 "文件名_p.后缀" 的路径
						string directory = System.IO.Path.GetDirectoryName(selectedPath);
						string fileNameOnly = System.IO.Path.GetFileNameWithoutExtension(selectedPath);
						string extension = System.IO.Path.GetExtension(selectedPath);
						string pPath = System.IO.Path.Combine(directory, fileNameOnly + "_p" + extension);

						// 保存原尺寸图 A 到 "文件名_p.后缀"
						SaveBitmapSourceToFile(finalResult, pPath);

						// 创建缩小一半的图片
						ScaleTransform scale = new ScaleTransform(0.5, 0.5);
						TransformedBitmap scaledBitmap = new TransformedBitmap(finalResult, scale);

						// 保存缩小后的图到用户选中的路径 (selectedPath)
						SaveBitmapSourceToFile(scaledBitmap, selectedPath);

						MessageBox.Show($"已双重保存：\n1. 原图：{System.IO.Path.GetFileName(pPath)}\n2. 50%缩略图：{System.IO.Path.GetFileName(selectedPath)}");

					} else {
						SaveBitmapSourceToFile(finalResult, selectedPath);
						MessageBox.Show("保存成功！");
					}
				} catch(Exception ex) {
					MessageBox.Show("保存失败: " + ex.Message);
				} finally {
					// 恢复 UI
					MaskOverlayImage.Visibility = Visibility.Visible;
					ExportGuide.Visibility = Visibility.Visible;
				}
			}
		}

		// 核心函数：手动混合像素
		private WriteableBitmap ApplyMaskToImage(BitmapSource image, BitmapSource mask) {
			int w = (int)_maskWidth;
			int h = (int)_maskHeight;

			// 确保格式统一
			FormatConvertedBitmap imgConv = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
			FormatConvertedBitmap maskConv = new FormatConvertedBitmap(mask, PixelFormats.Bgra32, null, 0);

			int stride = w * 4;
			byte[] imgPixels = new byte[h * stride];
			byte[] maskPixels = new byte[h * stride];

			imgConv.CopyPixels(imgPixels, stride, 0);
			maskConv.CopyPixels(maskPixels, stride, 0);

			// 逐像素处理
			for(int i = 0;i < imgPixels.Length;i += 4) {
				// imgPixels[i] 是 B, [i+1] 是 G, [i+2] 是 R, [i+3] 是 A

				byte aImg = imgPixels[i + 3];   // 图片原始透明度
				byte aMask = maskPixels[i + 3]; // 遮罩原始透明度

				// 最终透明度 = 图片透明度 * 遮罩透明度
				// 这样如果图片没盖到的地方(aImg=0)，结果就是 0（透明）
				// 如果遮罩是透明的地方(aMask=0)，结果也是 0（透明）
				imgPixels[i + 3] = (byte)((aImg * aMask) / 255);
			}

			WriteableBitmap result = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
			result.WritePixels(new Int32Rect(0, 0, w, h), imgPixels, stride, 0);
			return result;
		}

		private void SaveBitmapSourceToFile(BitmapSource source, string filePath) {
			PngBitmapEncoder encoder = new PngBitmapEncoder();
			encoder.Frames.Add(BitmapFrame.Create(source));
			using(var stream = System.IO.File.Create(filePath)) {
				encoder.Save(stream);
			}
		}

		// 更改画布样式
		private void BgColor_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			if(MainCanvas == null) return;
			var item = (sender as ComboBox).SelectedItem as ComboBoxItem;
			string tag = item.Tag.ToString();

			if(tag == "Checkered")
				MainCanvas.Background = (Brush)this.FindResource("CheckeredBrush");
			else
				MainCanvas.Background = (Brush)new BrushConverter().ConvertFromString(tag);
		}
	}
}