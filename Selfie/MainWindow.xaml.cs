//--------------------------------------------------------------------------------------
// Copyright 2015 Intel Corporation
// All Rights Reserved
//
// Permission is granted to use, copy, distribute and prepare derivative works of this
// software for any purpose and without fee, provided, that the above copyright notice
// and this statement appear in all copies.  Intel makes no representations about the
// suitability of this software for any purpose.  THIS SOFTWARE IS PROVIDED "AS IS."
// INTEL SPECIFICALLY DISCLAIMS ALL WARRANTIES, EXPRESS OR IMPLIED, AND ALL LIABILITY,
// INCLUDING CONSEQUENTIAL AND OTHER INDIRECT DAMAGES, FOR THE USE OF THIS SOFTWARE,
// INCLUDING LIABILITY FOR INFRINGEMENT OF ANY PROPRIETARY RIGHTS, AND INCLUDING THE
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE.  Intel does not
// assume any responsibility for any errors which may appear in this software nor any
// responsibility to update it.
//--------------------------------------------------------------------------------------
using System;
using System.Windows;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Mail;
using System.Net;


namespace BackgroundReplacementSample
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private PXCMSession session;
        private PXCMSenseManager senseManager;
        private Thread acquireThread;
        private volatile Bitmap backdrop;

        private const int HEIGHT = 480;
        private const int WIDTH = 640;
        private bool captureSnapshot = false;
        private string path = "C:\\Users\\lenovo\\Desktop\\selfie\\";
        private string img1 = "1st.jpg";
        private string img2 = "2nd.jpg";
        private string img3 = "3rd.jpg";
        private string emailID = "";
        private bool click = false;
 
        private int _counter = 9;


        System.Windows.Forms.Timer dispatcherTimer = new System.Windows.Forms.Timer();


        private void dispatcherTimer_Tick(object sender, EventArgs e)
        {
            _counter--;
            
            if (_counter == 3)
            {
                captureSnapshot = true;
            }
            else if (_counter > 3)
            {
                timer.Content = Convert.ToString(_counter - 3);
            }
            else if (_counter > 0 && _counter <= 2)
            {
                timer.Content = "Thank You";
                //thankyou.Content = "Check your Email.";
            }
            if (_counter == 0)
            {
                _counter = 9;
                timer.Content = " ";
                thankyou.Content = " ";
                dispatcherTimer.Stop();
            }
            
        }

        public MainWindow()
        {
            InitializeComponent();

            dispatcherTimer.Tick += new EventHandler(dispatcherTimer_Tick);
            dispatcherTimer.Interval = 1000;

            //Try to create a Session and SenseManager instance
            if (ConfigureRealSense())
            {

                Bitmap fileBitmap = new Bitmap(path + img1);  //background-image path

                 
                backdrop = new Bitmap(WIDTH, HEIGHT);

                using (Graphics g = Graphics.FromImage(backdrop))
                {
                    g.DrawImage(fileBitmap, 0, 0, WIDTH, HEIGHT);
                }

                fileBitmap.Dispose();

                // Start the acquisition thread
                acquireThread = new Thread(new ThreadStart(AcquireThread));
                acquireThread.Start();
            }
            else
            {
                System.Windows.MessageBox.Show("Unable to configure the RealSense camera! Click OK to close the program.", "Error");
                this.Close();
            }
        }

        private bool ConfigureRealSense()
        {
            try
            {
                // Create a session instance
                 session = PXCMSession.CreateInstance();

                // Create a SenseManager instance from the Session instance
                senseManager = session.CreateSenseManager();

                // Activate user segmentation
                senseManager.Enable3DSeg();

                // Specify image width and height of color stream
                senseManager.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_COLOR, WIDTH, HEIGHT, 30);

                // Initialize the pipeline
                senseManager.Init();

                // Mirror the image horizontally
                senseManager.QueryCaptureManager().QueryDevice().SetMirrorMode(PXCMCapture.Device.MirrorMode.MIRROR_MODE_HORIZONTAL);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void ReleaseResources()
        {
            if (acquireThread != null) { acquireThread.Abort(); }
            if (backdrop != null) { backdrop.Dispose(); }
            if (senseManager != null) { senseManager.Close(); }
            if (session != null) { session.Dispose(); }
        }

        private void AcquireThread()
        {
            // Stream data
            while (senseManager.AcquireFrame(true) >= pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                if (click == true)
                {
                    Thread.Sleep(500);
                    click = false;
                }
                // Retrieve the results
                PXCM3DSeg segmentation = senseManager.Query3DSeg();

                if (segmentation != null)
                {
                    // Get the segmented image
                    PXCMImage segmentedImage = segmentation.AcquireSegmentedImage();

                    if (segmentedImage != null)
                    {
                        // Access the segmented image data
                        PXCMImage.ImageData segmentedImageData;
                        segmentedImage.AcquireAccess(PXCMImage.Access.ACCESS_READ, PXCMImage.PixelFormat.PIXEL_FORMAT_RGB32, out segmentedImageData);

                        // Lock the backdrop image bitmap bits into system memory and access its data
                        // (Reference: https://msdn.microsoft.com/en-us/library/5ey6h79d%28v=vs.110%29.aspx
                        // (Reference: http://csharpexamples.com/fast-image-processing-c/)
                        Rectangle imageRect = new Rectangle(0, 0, WIDTH, HEIGHT);
                        BitmapData backdropBitmapData = backdrop.LockBits(imageRect, ImageLockMode.ReadWrite, backdrop.PixelFormat);
                        int bytesPerPixel = Bitmap.GetPixelFormatSize(backdropBitmapData.PixelFormat) / 8;
                        int widthInBytes = WIDTH * bytesPerPixel;

                        for (int h = 0; h < HEIGHT; h++)
                        {
                            // Use unsafe keyword to work with pointers for faster image processing
                            // (Required setting: Project -> Properties -> Build -> Allow unsafe code)
                            unsafe
                            {
                                byte* segmentedImagePixel = (byte*)segmentedImageData.planes[0] + h * segmentedImageData.pitches[0];

                                for (int w = 0; w < widthInBytes; w = w + bytesPerPixel)
                                {
                                    byte* backdropPixel = (byte*)backdropBitmapData.Scan0 + (h * backdropBitmapData.Stride);

                                    // Substitute segmented background pixels (those containing an alpha channel of zero)
                                    // with pixels from the selected backdrop image, if the checkbox is selected
                                    if ((segmentedImagePixel[3] <= 0))
                                    {
                                        segmentedImagePixel[0] = backdropPixel[w];
                                        segmentedImagePixel[1] = backdropPixel[w + 1];
                                        segmentedImagePixel[2] = backdropPixel[w + 2];
                                    }

                                    segmentedImagePixel += 4;
                                }
                            }
                        }

                        // Unlock the backdrop image bitmap bits
                        backdrop.UnlockBits(backdropBitmapData);

                        // Export the image data to a bitmap
                        Bitmap bitmap = segmentedImageData.ToBitmap(0, segmentedImage.info.width, segmentedImage.info.height);

                        // Update the UI by delegating work to the Dispatcher associated with the UI thread
                        this.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(delegate()
                        {
                            imgBackdrop.Source = ImageUtils.ConvertBitmapToWpf(bitmap);
                        }));

                        // Optionally save a snapshot of the image (captureSnapshot is set in the Capture button's event handler)
                        if (captureSnapshot)
                        {
                            bitmap.Save(path + "MyPic.jpg", System.Drawing.Imaging.ImageFormat.Jpeg);
                            captureSnapshot = false;
                        }

                        segmentedImage.ReleaseAccess(segmentedImageData);
                        segmentedImage.Dispose();
                        segmentation.Dispose();
                        bitmap.Dispose();
                    }
                }

                // Resume next frame processing
                senseManager.ReleaseFrame();
            }
        }

        //for image 1 selection
        private void btnOpenimg1_Click(object sender, RoutedEventArgs e)
        {
            click = true;
            try
            {
                Bitmap fileBitmap = new Bitmap(path + img1);  //background-image path

                // Resize the bitmap image to match the size of the segmented image
                // (Reference: http://www.deltasblog.co.uk/code-snippets/c-resizing-a-bitmap-image/)
                //backdrop = new Bitmap(WIDTH, HEIGHT);
                
                using (Graphics g = Graphics.FromImage(backdrop))
                {
                    g.DrawImage(fileBitmap, 0, 0, WIDTH, HEIGHT);
                }

                fileBitmap.Dispose();
            }
            catch (Exception)
            {
                System.Windows.MessageBox.Show("Error opening the image.");
            }
        }

        //for image 2 selection
        private void btnOpenimg2_Click(object sender, RoutedEventArgs e)
        {
            click = true;
            try
            {
                Bitmap fileBitmap = new Bitmap(path + img2);  //background-image path

                // Resize the bitmap image to match the size of the segmented image
                // (Reference: http://www.deltasblog.co.uk/code-snippets/c-resizing-a-bitmap-image/)
                //backdrop = new Bitmap(WIDTH, HEIGHT);

                using (Graphics g = Graphics.FromImage(backdrop))
                {
                    g.DrawImage(fileBitmap, 0, 0, WIDTH, HEIGHT);
                }

                fileBitmap.Dispose();
            }
            catch (Exception)
            {
                System.Windows.MessageBox.Show("Error opening the image.");
            }
        }

        //for image 3 selection
        private void btnOpenimg3_Click(object sender, RoutedEventArgs e)
        {
            click = true;
            try
            {
                Bitmap fileBitmap = new Bitmap(path + img3);  //background-image path

                // Resize the bitmap image to match the size of the segmented image
                // (Reference: http://www.deltasblog.co.uk/code-snippets/c-resizing-a-bitmap-image/)
                //backdrop = new Bitmap(WIDTH, HEIGHT);

                using (Graphics g = Graphics.FromImage(backdrop))
                {
                    g.DrawImage(fileBitmap, 0, 0, WIDTH, HEIGHT);
                }

                fileBitmap.Dispose();
            }
            catch (Exception)
            {
                System.Windows.MessageBox.Show("Error opening the image.");
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ReleaseResources();
        }

        private void btnCapture_Click(object sender, RoutedEventArgs e)
        {
                   }
       
    }
}
