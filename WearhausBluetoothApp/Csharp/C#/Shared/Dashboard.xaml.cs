using SDKTemplate;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Common;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Media.Imaging;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace WearhausBluetoothApp
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class Dashboard : Page
    {

        List<string> InstructionImages = new List<string>();
        List<string> InstructionTexts = new List<string>();
        private int ImageFrame;

        public Dashboard()
        {
            this.InitializeComponent();

            RenderedArcConnState = ArcLink.ArcConnState.NoArc;

            MainPage.MyArcLink.ArcConnStateChanged += MyArcConnStateListener;
            MainPage.MyArcLink.DFUStepChanged += MyArcConnStateListener;

            ArcStateText.Text = "";
            InstructionLayout.Visibility = Visibility.Collapsed;

            InstructionImages.Add("arc_update_1.png");
            InstructionImages.Add("arc_update_2.png");
            InstructionImages.Add("arc_update_3.png");
            //InstructionTexts.Add("Step 1: Turn on your Wearhaus Arc. Then open Windows Bluetooth Settings by searching for Bluetooth Settings in the Windows Search Bar.");
            InstructionTexts.Add("Step 1: Turn on your Wearhaus Arc. Then open Windows Bluetooth Settings.");
            InstructionTexts.Add("Step 2: Find Wearhaus Arc and click \"Pair\". Click \"Yes\" to any prompts that ask for permission.");
            InstructionTexts.Add("Step 3: Wait for the progress bar to complete all the way after clicking Pair. After that, press Connect My Arc above.");

            UpdateImage(0);
        }

        // in case we want any popups or onetime UI actions when the state changes in this page
        private ArcLink.ArcConnState RenderedArcConnState;

        void MyArcConnStateListener(object sender, EventArgs e) {
            switch (MainPage.MyArcLink.MyArcConnState)
            {
                case ArcLink.ArcConnState.NoArc:
                    // TODO here, there should be the picture steps to help you
                    ArcStateText.Text = "";
                    ConnectButton.IsEnabled = true;
                    ConnectButton.Opacity = 1.0;
                    HowToButton.IsEnabled = true;
                    HowToButton.Opacity = 1.0;
                    ConnectionProgress.Opacity = 0;
                    break;

                case ArcLink.ArcConnState.TryingToConnect:
                    ArcStateText.Text = "connecting";
                    ConnectButton.IsEnabled = false;
                    ConnectButton.Opacity = 0.0;
                    HowToButton.IsEnabled = false;
                    HowToButton.Opacity = 0.0;
                    ConnectionProgress.Opacity = 1.0;
                    //case ArcLink.ArcConnState.GatheringInfo:
                    break;

                case ArcLink.ArcConnState.Connected:
                    ArcStateText.Text = "Connected to " + MainPage.MyArcLink.DeviceHumanName;
                    ConnectButton.IsEnabled = false;
                    ConnectButton.Opacity = 0.0;
                    HowToButton.IsEnabled = false;
                    HowToButton.Opacity = 0.0;
                    ConnectionProgress.Opacity = 1.0;
                    break;

                case ArcLink.ArcConnState.Error:
                    if (MainPage.MyArcLink.ErrorHuman != null && MainPage.MyArcLink.ErrorHuman.Length > 0)
                    {
                        ArcStateText.Text = MainPage.MyArcLink.ErrorHuman;
                    } else
                    {
                        ArcStateText.Text = "We've ran into a problem";
                    }


                    ConnectButton.Content = "Try Again";
                    ConnectButton.IsEnabled = true;
                    ConnectButton.Opacity = 1.0;
                    HowToButton.IsEnabled = true;
                    HowToButton.Opacity = 1.0;
                    ConnectionProgress.Opacity = 0;
                    MainPage.Current.NotifyUser(MainPage.MyArcLink.ErrorHuman,
                            NotifyType.ErrorMessage);
                    break;


            }

            RenderedArcConnState = MainPage.MyArcLink.MyArcConnState;

        }


        

        /// <summary>
        /// Message to start the UI Services and functions to search for nearby bluetooth devices
        /// </summary>
        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            MainPage.MyArcLink.ConnectArc();
        }



        private void previousItem_Click(object sender, RoutedEventArgs e)
        {
            UpdateImage(ImageFrame - 1);
        }
        private void nextItem_Click(object sender, RoutedEventArgs e)
        {
            UpdateImage(ImageFrame + 1);
        }

        private void UpdateImage(int newFrame)
        {
            newFrame = Math.Min(newFrame, InstructionTexts.Count);
            newFrame = Math.Max(newFrame, 0);

            ImageFrame = newFrame;

            if (ImageFrame >= InstructionImages.Count - 1)
            {
                NextButton.Opacity = 0.5;
                NextButton.IsEnabled = false;
                PreviousButton.Opacity = 1.0;
                PreviousButton.IsEnabled = true;
            }
            else if (ImageFrame <= 0)
            {
                NextButton.Opacity = 1.0;
                NextButton.IsEnabled = true;
                PreviousButton.Opacity = 0.5;
                PreviousButton.IsEnabled = false;
            } else
            {
                NextButton.Opacity = 1.0;
                NextButton.IsEnabled = true;
                PreviousButton.Opacity = 1.0;
                PreviousButton.IsEnabled = true;
            }

            InstructionImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/" + InstructionImages[ImageFrame].ToString()));
            InstructionText.Text = InstructionTexts[ImageFrame];
        }

        private void InfoViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {

        }
        private Boolean InstrucVisible = false;
        private void HowToButton_Click(object sender, RoutedEventArgs e)
        {
            InstrucVisible = !InstrucVisible;
            if (InstrucVisible)
            {
                InstructionLayout.Visibility = Visibility.Visible;
            } else
            {
                InstructionLayout.Visibility = Visibility.Collapsed;
            }


            
        }


    }
}
