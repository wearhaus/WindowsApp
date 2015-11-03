using WearhausBluetoothApp.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

using SDKTemplate.Common;

using WearhausBluetoothApp;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace WearhausBluetoothApp
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class InstructionsPage : Page
    {
        //private WearhausHttpController HttpController;
        private NavigationHelper navigationHelper;
        private ObservableDictionary defaultViewModel = new ObservableDictionary();

        /// <summary>
        /// This can be changed to a strongly typed view model.
        /// </summary>
        public ObservableDictionary DefaultViewModel
        {
            get { return this.defaultViewModel; }
        }

        /// <summary>
        /// NavigationHelper is used on each page to aid in navigation and 
        /// process lifetime management
        /// </summary>
        public NavigationHelper NavigationHelper
        {
            get { return this.navigationHelper; }
        }


        /// <summary>
        /// Populates the page with content passed during navigation. Any saved state is also
        /// provided when recreating a page from a prior session.
        /// </summary>
        /// <param name="sender">
        /// The source of the event; typically <see cref="NavigationHelper"/>
        /// </param>
        /// <param name="e">Event data that provides both the navigation parameter passed to
        /// <see cref="Frame.Navigate(Type, Object)"/> when this page was initially requested and
        /// a dictionary of state preserved by this page during an earlier
        /// session. The state will be null the first time a page is visited.</param>
        private void navigationHelper_LoadState(object sender, LoadStateEventArgs e)
        {
        }

        /// <summary>
        /// Preserves state associated with this page in case the application is suspended or the
        /// page is discarded from the navigation cache.  Values must conform to the serialization
        /// requirements of <see cref="SuspensionManager.SessionState"/>.
        /// </summary>
        /// <param name="sender">The source of the event; typically <see cref="NavigationHelper"/></param>
        /// <param name="e">Event data that provides an empty dictionary to be populated with
        /// serializable state.</param>
        private void navigationHelper_SaveState(object sender, SaveStateEventArgs e)
        {
        }

        #region NavigationHelper registration

        /// The methods provided in this section are simply used to allow
        /// NavigationHelper to respond to the page's navigation methods.
        /// 
        /// Page specific logic should be placed in event handlers for the  
        /// <see cref="GridCS.Common.NavigationHelper.LoadState"/>
        /// and <see cref="GridCS.Common.NavigationHelper.SaveState"/>.
        /// The navigation parameter is available in the LoadState method 
        /// in addition to page state preserved during an earlier session.

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            navigationHelper.OnNavigatedTo(e);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            navigationHelper.OnNavigatedFrom(e);
        }

        #endregion

        List<string> Images = new List<string>();
        List<string> InstructionTexts = new List<string>();
        int ImageCount;

        public InstructionsPage()//WearhausHttpController httpController)
        {
            this.InitializeComponent();

            this.navigationHelper = new NavigationHelper(this);
            this.navigationHelper.LoadState += navigationHelper_LoadState;
            this.navigationHelper.SaveState += navigationHelper_SaveState;
            //HttpController = httpController;

            ImageCount = 0;
            PreviousButton.IsEnabled = false;

            Images.Add("arc_update_step1.png");
            Images.Add("arc_update_step2.png");
            Images.Add("arc_update_step3.png");
            InstructionTexts.Add("Step 1: Turn on your Wearhaus Arc! Then open Windows Bluetooth Settings by searching for Bluetooth Settings in the Windows Search Bar. ");
            InstructionTexts.Add("Step 2: Find Wearhaus Arc and click \"Pair\". Click \"Yes\" to any prompts that ask for permission. ");
            InstructionTexts.Add("Step 3: Wait for the progress bar to complete all the way after clicking Pair. After that, hit Ready below!");
            InstructionImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/" + Images[ImageCount]));
            InstructionText.Text = InstructionTexts[ImageCount];
        }

        private void ReadyButton_Click(object sender, RoutedEventArgs e)
        {
            Frame rootFrame = Window.Current.Content as Frame;

            rootFrame.Navigate(typeof(SDKTemplate.MainPage));
        }

        private void previousItem_Click(object sender, RoutedEventArgs e)
        {
            if (Images != null)
            {
                ImageCount--;
                if (ImageCount >= 0)
                {
                    ImageRotation();
                }
                else
                {
                    ImageCount = Images.Count - 1;
                    ImageRotation();
                }
            }
        }
        private void nextItem_Click(object sender, RoutedEventArgs e)
        {
            if (Images != null)
            {
                ImageCount++;
                if (ImageCount < Images.Count)
                {
                    ImageRotation();
                }
                else
                {
                    ImageCount = 0;
                    ImageRotation();
                }
            }
        }

        private void ImageRotation()
        {
            if (ImageCount == Images.Count-1)
            {
                NextButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                ReadyButton.Visibility = Windows.UI.Xaml.Visibility.Visible;
            }
            else
            {
                NextButton.Visibility = Windows.UI.Xaml.Visibility.Visible;
                ReadyButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            }


            if (ImageCount == 0)
            {
                PreviousButton.IsEnabled = false;
            }
            else
            {
                PreviousButton.IsEnabled = true;
            }

            InstructionImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/" + Images[ImageCount].ToString()));
            InstructionText.Text = InstructionTexts[ImageCount];
        }
    }
}

