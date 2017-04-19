using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

using AForge;
using AForge.Imaging.Filters;
using AForge.Video.DirectShow;

using MathNet.Numerics;

namespace roadTrack
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        Bitmap image_bw, image_gray;

        List<IntPoint> corners;

        QuadrilateralTransformation filter_quadrilateral;
        Threshold filter_binarization;
        Pixellate filter_pixellate;
        Threshold filter_binarization_2;
        Crop filter_crop;

        double[] leftArrayWidth = new double[0];
        double[] leftArrayHeight = new double[0];
        double[] rightArrayWidth = new double[0];
        double[] rightArrayHeight = new double[0];

        double avgBetween = 150;

        int framesNum = 0;

        double top_left,
               top_right,
               bottom_left,
               bottom_right;

        double leftLine,
               rightLine;

        bool block = false,
             block2 = false;

        int start = 0,
            stop = 0,
            length = 0;

        int memoryFrames = 0;

        private const int statLength = 15;
        private int statIndex = 0;
        private int statReady = 0;
        private int[] statCount = new int[statLength];

        private void button1_Click(object sender,
                                   EventArgs e)
        {
            //Настройка птичьего вида
            corners = new List<IntPoint>();

            int srez_top = 0,
                srez_bottom = 135,
                srez_left = 0,
                srez_right = 640,
                alpha = -1000;

            corners.Add(new IntPoint(srez_left,
                                     srez_top));
            corners.Add(new IntPoint(srez_right,
                                     srez_top));
            corners.Add(new IntPoint(srez_right - alpha,
                                     srez_bottom));
            corners.Add(new IntPoint(srez_left + alpha,
                                     srez_bottom));

            //Настройка фильтров
            filter_quadrilateral = new QuadrilateralTransformation(corners,
                                                                   640,
                                                                   765);
            filter_binarization = new Threshold(185);
            filter_pixellate = new Pixellate(4,
                                             4);
            filter_binarization_2 = new Threshold(1);

            //Настройка обрезания
            int cut_top = 0,
                cut_bottom = 765,
                cut_left = 140,
                cut_right = 640 - 2 * cut_left;

            filter_crop = new Crop(new Rectangle(cut_left,
                                                 cut_top,
                                                 cut_right,
                                                 cut_bottom));

            //Подключаем видео
            if (radioButton1.Checked)
            {
                FilterInfoCollection videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                VideoCaptureDevice fileSource = new VideoCaptureDevice(videoDevices[0].MonikerString);

                var form = new VideoCaptureDeviceForm();

                if (form.ShowDialog() == DialogResult.OK)
                {
                    videoSourcePlayer1.VideoSource = form.VideoDevice;
                }
            }
            else if (radioButton2.Checked)
            {
                FileVideoSource fileSource = new FileVideoSource("1.avi");
                videoSourcePlayer1.VideoSource = fileSource;
            }

            videoSourcePlayer1.Start();
            timer1.Start();
        }

        private void Form1_FormClosed(object sender,
                                      FormClosedEventArgs e)
        {
            videoSourcePlayer1.Stop();
            timer1.Stop();
        }

        private void videoSourcePlayer1_NewFrame(object sender,
                                                 ref Bitmap inputImage)
        {
            if (framesNum > 1)
            {
                //Загружаем
                Bitmap workImage = (Bitmap)inputImage.Clone();

                //Делаем вид от птичьего лица
                image_bw = filter_quadrilateral.Apply(workImage);
                image_gray = image_bw;

                //Обрезаем
                image_bw = filter_crop.Apply(image_bw);
                image_gray = filter_crop.Apply(image_gray);

                Graphics g = Graphics.FromImage(image_gray);

                //Обесцвечиваем
                image_bw = Grayscale.CommonAlgorithms.RMY.Apply(image_bw);

                //Бинаризируем
                CannyEdgeDetector canny = new CannyEdgeDetector(10, 25);
                canny.ApplyInPlace(image_bw);

                //Бинаризируем
                image_bw = filter_binarization_2.Apply(image_bw);

                //Ищем линию разметки
                detectLine("leftLine");
                detectLine("rightLine");

                //Сглаживаем   
                if (leftArrayWidth.Length > 2)
                {
                    leftArrayWidth = smoothing(leftArrayWidth);
                }
                if (rightArrayWidth.Length > 2)
                {
                    rightArrayWidth = smoothing(rightArrayWidth);
                }

                //Проверяем полноту массивов
                if ((rightArrayWidth.Length > 4) &&
                    (leftArrayWidth.Length < 5))
                {
                    full("leftLine",
                         leftArrayWidth,
                         leftArrayHeight,
                         rightArrayWidth,
                         rightArrayHeight,
                         out leftArrayWidth,
                         out leftArrayHeight);
                }

                if ((leftArrayWidth.Length > 4) &&
                    (rightArrayWidth.Length < 5))
                {
                    full("rightLine",
                         rightArrayWidth,
                         rightArrayHeight,
                         leftArrayWidth,
                         leftArrayHeight,
                         out rightArrayWidth,
                         out rightArrayHeight);
                }

                //Интерполяция
                if (leftArrayWidth.Length > 4)
                {
                    leftLine = interpolation(leftArrayHeight,
                                             leftArrayWidth,
                                             "leftLine");

                    memoryFrames = 10;
                }

                if (rightArrayWidth.Length > 4)
                {
                    rightLine = interpolation(rightArrayHeight,
                                              rightArrayWidth,
                                              "rightLine");

                    memoryFrames = 10;
                }

                if (memoryFrames > 0)
                {
                    g.DrawLine(new Pen(Color.Yellow, 2),
                               (float)top_left,
                               0,
                               (float)bottom_left,
                               765);

                    g.DrawLine(new Pen(Color.Yellow, 2),
                               (float)top_right,
                               0,
                               (float)bottom_right,
                               765);

                    //Нечеткая логика
                    fuzzy(leftLine,
                          rightLine);

                    memoryFrames--;

                }

                if (checkBox1.Checked)
                {
                    //Рисуем центральную линию
                    g.DrawLine(new Pen(Color.Blue, 2),
                               image_gray.Width / 2,
                               0,
                               image_gray.Width / 2,
                               765);

                    //Рисуем линии разметки
                    lines(leftArrayWidth,
                          leftArrayHeight);
                    lines(rightArrayWidth,
                          rightArrayHeight);
                }

                //Опустошаем массивы
                Array.Resize(ref leftArrayWidth,
                             0);
                Array.Resize(ref leftArrayHeight,
                             0);
                Array.Resize(ref rightArrayWidth,
                             0);
                Array.Resize(ref rightArrayHeight,
                             0);

                //Отрисовываем
                pictureBox1.Image = image_bw;
                pictureBox1.Invalidate();
                pictureBox2.Image = image_gray;
                pictureBox2.Invalidate();
            }

            framesNum++;
        }

        private void detectLine(string name)
        {
            for (int h = 760; h > 0; h = h - 1)
            {
                if (name == "leftLine")
                {
                    for (int w = image_bw.Width / 2 - 1; w > 100; w--)
                    {
                        detect(name,
                               w,
                               h,
                               leftArrayWidth,
                               leftArrayHeight,
                               out leftArrayWidth,
                               out leftArrayHeight);
                    }
                }
                else if (name == "rightLine")
                {
                    for (int w = image_bw.Width / 2 + 1; w < 260; w++)
                    {
                        detect(name,
                               w,
                               h,
                               rightArrayWidth,
                               rightArrayHeight,
                               out rightArrayWidth,
                               out rightArrayHeight);
                    }
                }

                block = false;
                block2 = false;

                start = 0;
                stop = 0;
                length = 0;
            }
        }

        private void detect(string name,
                            int w,
                            int h,
                            double[] inWidth,
                            double[] inHeight,
                            out double[] width,
                            out double[] height)
        {
            Color pixelColor = image_bw.GetPixel(w, h);

            if (pixelColor.Name != "ff000000" &&
                block == false &&
                block2 == false)
            {
                block = true;

                start = w;
            }

            if (block == true &&
                block2 == false &&
                pixelColor.Name != "ff000000" &&
                w != start)
            {
                stop = w;

                if (name == "leftLine")
                {
                    length = start - stop;
                }
                else if (name == "rightLine")
                {
                    length = stop - start;
                }

                block2 = true;

                if (length > 1 && length < 5)
                {
                    Array.Resize(ref inWidth,
                                 inWidth.Length + 1);

                    if (name == "leftLine")
                    {
                        inWidth[inWidth.Length - 1] = w + length / 2;
                    }
                    else if (name == "rightLine")
                    {
                        inWidth[inWidth.Length - 1] = w - length / 2;
                    }

                    Array.Resize(ref inHeight,
                                 inHeight.Length + 1);
                    inHeight[inHeight.Length - 1] = h;
                }
                else
                {
                    block = false;
                    block2 = false;
                }
            }

            width = inWidth;
            height = inHeight;
        }

        private void full(string name,
                          double[] pasteWidth,
                          double[] pasteHeight,
                          double[] copyWidth,
                          double[] copyHeight,
                          out double[] width,
                          out double[] height)
        {
            Array.Resize(ref pasteWidth,
                         0);
            Array.Resize(ref pasteHeight,
                         0);

            for (int i = 0; i < copyWidth.Length; i++)
            {
                Array.Resize(ref pasteWidth,
                             pasteWidth.Length + 1);

                if (name == "rightLine")
                {
                    pasteWidth[pasteWidth.Length - 1] = copyWidth[i] + avgBetween;
                }
                else if (name == "leftLine")
                {
                    pasteWidth[pasteWidth.Length - 1] = copyWidth[i] - avgBetween;
                }

                Array.Resize(ref pasteHeight,
                             pasteHeight.Length + 1);
                pasteHeight[pasteHeight.Length - 1] = copyHeight[i];
            }

            width = pasteWidth;
            height = pasteHeight;
        }

        private double[] smoothing(double[] input)
        {
            double[] smoothed = new double[input.Length];

            smoothed[0] = (input[0] + input[1]) / 2;

            for (int i = 1; i < smoothed.Length - 1; i++)
            {
                smoothed[i] = (input[i - 1] + input[i] + input[i + 1]) / 3;
            }

            smoothed[smoothed.Length - 1] = (input[smoothed.Length - 2] + input[smoothed.Length - 1]) / 2;

            return smoothed;
        }

        private double interpolation(double[] arrayHeight,
                                     double[] arrayWidth,
                                     string name)
        {
            var spline = Interpolate.Linear(arrayHeight,
                                            arrayWidth);

            if (name == "leftLine")
            {
                top_left = spline.Interpolate(0);
                bottom_left = spline.Interpolate(765);
            }
            else if (name == "rightLine")
            {
                top_right = spline.Interpolate(0);
                bottom_right = spline.Interpolate(765);
            }

            double c = spline.Interpolate(715);

            return c;
        }

        void fuzzy(double leftLine,
                   double rightLine)
        {
            double betweenLines = rightLine - leftLine;

            if (betweenLines > 75 &&
                betweenLines < 200)
            {
                avgBetween = (avgBetween + betweenLines) / 2;
            }

            double L1 = 0,
                   L2 = leftLine + 0.25 * betweenLines,
                   L3 = leftLine + 0.45 * betweenLines,
                   R3 = rightLine - 0.45 * betweenLines,
                   R2 = rightLine - 0.25 * betweenLines,
                   R1 = image_bw.Width;

            Graphics g = Graphics.FromImage(image_gray);

            if (checkBox1.Checked)
            {
                g.DrawLine(new Pen(Color.Red, 2),
                           (float)L1,
                           715,
                           (float)L2,
                           715);
                g.DrawLine(new Pen(Color.Orange, 2),
                           (float)L2,
                           715,
                           (float)L3,
                           715);
                g.DrawLine(new Pen(Color.Green, 2),
                           (float)L3,
                           715,
                           (float)R3,
                           715);
                g.DrawLine(new Pen(Color.Orange, 2),
                           (float)R3,
                           715,
                           (float)R2,
                           715);
                g.DrawLine(new Pen(Color.Red, 2),
                           (float)R2,
                           715,
                           (float)R1,
                           715);
            }

            //Хорошо
            if (image_bw.Width / 2 > L3 &&
                image_bw.Width / 2 < R3)
            {
                g.FillRectangle(new SolidBrush(Color.Green),
                                (float)leftLine,
                                715,
                                (float)(rightLine - leftLine),
                                50);
            }

            //Чуть правее
            if (image_bw.Width / 2 > L2 &&
                image_bw.Width / 2 < L3)
            {
                g.FillRectangle(new SolidBrush(Color.Orange),
                                (float)rightLine,
                                715,
                                (float)(image_bw.Width - rightLine),
                                50);
            }

            //Чуть левее
            if (image_bw.Width / 2 > R3 &&
                image_bw.Width / 2 < R2)
            {
                g.FillRectangle(new SolidBrush(Color.Orange),
                                0,
                                715,
                                (float)leftLine,
                                50);
            }

            //Сильно правее
            if (image_bw.Width / 2 > L1 &&
                image_bw.Width / 2 < L2)
            {
                g.FillRectangle(new SolidBrush(Color.Red),
                                (float)rightLine,
                                715,
                                (float)(image_bw.Width - rightLine),
                                50);
            }

            //Чуть левее
            if (image_bw.Width / 2 > R2 &&
                image_bw.Width / 2 < R1)
            {
                g.FillRectangle(new SolidBrush(Color.Red),
                                0,
                                715,
                                (float)leftLine,
                                50);
            }
        }

        private void timer1_Tick(object sender,
                                 EventArgs e)
        {
            if (videoSourcePlayer1.VideoSource != null)
            {
                statCount[statIndex] = videoSourcePlayer1.VideoSource.FramesReceived;

                if (++statIndex >= statLength)
                {
                    statIndex = 0;
                }

                if (statReady < statLength)
                {
                    statReady++;
                }

                float fps = 0;

                for (int i = 0; i < statReady; i++)
                {
                    fps += statCount[i];
                }

                fps /= statReady;

                label1.Text = "FPS: " + fps.ToString();
                label2.Text = "Дистанция: " + avgBetween.ToString();
                label3.Text = "Кадров: " + framesNum.ToString();
            }
        }

        private void lines(double[] arrayWidth,
                           double[] arrayHeight)
        {
            Graphics g = Graphics.FromImage(image_gray);

            for (int i = 0; i < arrayWidth.Length - 1; i++)
            {
                g.DrawLine(new Pen(Color.Red, 2),
                           (float)arrayWidth[i],
                           (float)arrayHeight[i],
                           (float)arrayWidth[i + 1],
                           (float)arrayHeight[i + 1]);
            }
        }
    }
}
