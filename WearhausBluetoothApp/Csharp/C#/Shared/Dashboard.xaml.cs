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

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace WearhausBluetoothApp
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class Dashboard : Page
    {
        public Dashboard()
        {
            this.InitializeComponent();

            RenderedArcConnState = ArcLink.ArcConnState.NoArc;

            MainPage.MyArcLink.ArcConnStateChanged += MyArcConnStateListener;
            MainPage.MyArcLink.DFUStepChanged += MyArcConnStateListener;
        }

        // in case we want any popups or onetime UI actions when the state changes in this page
        private ArcLink.ArcConnState RenderedArcConnState;

        void MyArcConnStateListener(object sender, EventArgs e) {
            switch (MainPage.MyArcLink.MyArcConnState)
            {
                case ArcLink.ArcConnState.NoArc:
                    // TODO here, there should be the picture steps to help you
                    ArcStateText.Text = "No Arc connected";
                    ConnectButton.IsEnabled = true;
                    ConnectButton.Visibility = Visibility.Visible;
                    ConnectionProgress.Visibility = Visibility.Collapsed;
                    break;

                case ArcLink.ArcConnState.TryingToConnect:
                    ArcStateText.Text = "connecting";
                    ConnectButton.IsEnabled = false;
                    ConnectButton.Visibility = Visibility.Collapsed;
                    ConnectionProgress.Visibility = Visibility.Visible;
                    //case ArcLink.ArcConnState.GatheringInfo:
                    break;

                case ArcLink.ArcConnState.Connected:
                    ArcStateText.Text = "Connected to " + MainPage.MyArcLink.DeviceHumanName;
                    ConnectButton.IsEnabled = false;
                    ConnectButton.Visibility = Visibility.Visible;
                    ConnectionProgress.Visibility = Visibility.Visible;
                    break;

                case ArcLink.ArcConnState.Error:
                    ArcStateText.Text = "We've ran into a problem";
                    ConnectButton.IsEnabled = false;
                    ConnectButton.Visibility = Visibility.Visible;
                    ConnectionProgress.Visibility = Visibility.Visible;
                    MainPage.Current.NotifyUser(MainPage.MyArcLink.ErrorHuman,
                            NotifyType.ErrorMessage);
                    break;


            }

            
        }

        /// <summary>
        /// Message to start the UI Services and functions to search for nearby bluetooth devices
        /// </summary>
        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            MainPage.MyArcLink.ConnectArc();
        }


        private void DebugButton_Click(object sender, RoutedEventArgs e)
        {
        }
        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
        }

        private void InfoViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {

        }

    }
}
