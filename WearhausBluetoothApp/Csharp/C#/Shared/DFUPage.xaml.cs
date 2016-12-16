using Common;
using SDKTemplate;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using static Common.ArcLink;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace WearhausBluetoothApp
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class DFUPage : Page
    {
        public DFUPage()
        {
            this.InitializeComponent();


            MainPage.MyArcLink.ArcConnStateChanged += UpdateUIListener;
            MainPage.MyArcLink.DFUStepChanged += UpdateUIListener;
            MainPage.MyHttpController.AccountStateChanged += UpdateUIListener;

            UpdateUIListener(null, null);
        }


        void UpdateUIListener(object sender, EventArgs e)
        {
            VerifyButton.Visibility = Visibility.Collapsed;

            if (MainPage.MyArcLink.MyDFUStep == DFUStep.None)
            {
                DfuStateText.Text = "Firmware Update will take 10-15 minutes. Make sure your headphones remain powered on. Don't play music for the duration of the update.";
                DfuProgress.Opacity = 0;
                StartButton.Opacity = 1.0;
                StartButton.IsEnabled = true;
            } else 
            {
                DfuProgress.Opacity = 1.0;
                StartButton.Opacity = 0.0;
                StartButton.IsEnabled = false;

                switch (MainPage.MyArcLink.MyDFUStep)
                {
                    case DFUStep.StartingUpload:
                        DfuStateText.Text = "Starting Upload";
                        break;
                    case DFUStep.UploadingFW:
                        DfuStateText.Text = "Uploading";
                        break;
                    case DFUStep.VerifyingImage:
                        DfuStateText.Text = "Verifying";
                        break;
                    case DFUStep.ChipPowerCycle:
                        DfuStateText.Text = "Your Arc will automatically restart - please listen to your Arc for a double beep sound to indicate a restart. When you hear the beep or have waited 30 seconds, please press the \"Verify Update\" button to verify that the update worked."; // TODO
                        break;
                    case DFUStep.AwaitingManualPowerCycle:
                        VerifyButton.Visibility = Visibility.Visible;
                        DfuProgress.Opacity = 0.0;
                        DfuStateText.Text = "Please turn your Arc off (hold power button for 5 seconds). Then turn it back on and reconnect the Arc under Bluetooth Settings";
                        break;
                    case DFUStep.Success:
                        DfuProgress.Opacity = 0.0;
                        DfuStateText.Text = "Firmware Update succeeded!"; // TODO
                        break;
                    case DFUStep.Error:
                        DfuProgress.Opacity = 0.0;
                        DfuStateText.Text = ArcUtil.GetHumanFromDfuResultStatus(MainPage.MyArcLink.MyDFUResultStatus);
                        break;

                }

            }

        }


        private void StartButton_Click(object sender, RoutedEventArgs e)
        {

            //MainPage.MyArcLink.StartDFURequest();
            PickFileButton_Click(null, null);
        }


        private StorageFile DfuFile;
        //private DataReader DfuReader;

        private async void PickFileButton_Click(object sender, RoutedEventArgs e)
        {
            // Open DFU File Buffer and DataReader
            FileOpenPicker filePicker = new FileOpenPicker();
            filePicker.SuggestedStartLocation = PickerLocationId.Downloads;
            filePicker.FileTypeFilter.Clear();
            filePicker.FileTypeFilter.Add("*");
#if WINDOWS_PHONE_APP
            filePicker.PickSingleFileAndContinue();
#else
            DfuFile = await filePicker.PickSingleFileAsync();
#endif

            if (DfuFile == null)
            {
                return;
            }

            // Get CRC first from File
            var buf = await FileIO.ReadBufferAsync(DfuFile);
            //DfuReader = DataReader.FromBuffer(buf);
            uint fileSize = buf.Length;
            byte[] fileBuffer = new byte[fileSize];
            //DfuReader.ReadBytes(fileBuffer);
            //GaiaHandler.SetFileBuffer(fileBuffer);


            MainPage.MyArcLink.StartDFURequest(fileBuffer, null);

            //Instructions.Text = "Picked File: " + DfuFile.Name + ". Press Send DFU to Begin Update, or Pick File again.";
        }


        private void VerifyButton_Click(object sender, RoutedEventArgs e)
        {
            MainPage.MyArcLink.ConnectArc();

        }
        
    }
}
