# WindowsApp
Code for Windows / Windows Phone app 

This repo is intended for Visual Studio 2015; it may work with other versions though. This includes the code for both Windows desktop and phone which use some shared files as well as some independent files. In Nov 2015, both Windows desktop and phone had app packages generated and uploaded. On Dec 2016, only windows desktop had a new app package uploaded which updated it to Server v1.3, which includes segmenting on ProductId, and new UI. Windows Phone is still using the old server v1.2, which is still set up, but may be deprecated in the near future when ProductId segmentation is mandatory. Since we didn't have a working windows phone at time of update, we never tested the changes, which I'm fairly certain breaks some things, especially UI layout.


* open .sln in WearhausBluetoothApp directory. There is another project in the repo, it is a modified sample for development. Ignore it for now.
* You may have the error from a fresh git pull: "File 'Package.StoreAssociation.xml' not found"
	* Go to Project -> Store -> etc. Log in with richie@wearhaus.com into visual studio.
	* if greyed out: http://stackoverflow.com/questions/25438522/unable-to-create-app-package-the-appxupload-file-in-visual-studio-2013-for-a
		* just select project in the solution explorer
* The repo now includes the cert .pfx for generating app packages, this isn't insecure since you can always just generate a new one whenever. Keeping it in repo just saves a few hours of time. You can read this for more details: https://msdn.microsoft.com/en-us/library/windows/desktop/jj835832(v=vs.85).aspx	

* Initially, I attempted to refactor Arc and BT into a separate ArcLink class, but somewhere an issue popped up that broke DFU, so a fresh new attempt was done leaving all ArcLink stuff just in Scenario1_DfuClient.xaml.cs and using FSMs to keep UI organized.
* in Visual Studio 2015, there is a bug with 2 monitors and the .xaml preview. If you want to switch device from desktop/phone, you must go to the .xaml.cs file in the root monitor, NOT a second one. For some reason, it only appears in the original monitor.

* Issues exist with windows 8: An exception is thrown from the "await RfcommDeviceService.FromIdAsync(chatServiceInfo.Id);"  It is possibly related to "A message sent on a datagram socket was larger than the internal message before or some other network limit, or the buffer used to receive a datagram into was smaller than the datagram itself. (Exception from HRESULT: 0x80072738)".
	* Sidd was investigating this a year ago and found no solution. It breaks only for windows 8, not 10, so we can't test it in the office, but it doesn't appear to be a big deal as of now.



Control Flow for Windows Desktop:
* Starts with App.xaml.cs, it calls OnLaunched(), which creates a new Frame and navigates it to MainPage.
* MainPage.xaml.cs. This has some weird boilerplate code that is moslty useless but left in since it wasn't worth removing. It just reads SampleConfiguration.cs for what page to create, which is listed as Scenario1_DfuClient. Then it navigates its Frame to it.
* Scenario1_DfuClient.xaml.cs follows the ArcConnState FSM, which displays UI textblocks/buttons/images to help user connect device. This creates the IO stream to the Arc and the parse loop. Once connected, it registers the Headphone on the server and checks the live FirmwareTable to see what the latest target Firmware Version is. This allows us to control remotely if we want to pause the update (ex/ something went wrong) or push out new version without needing to update the windows app. If allowed to start an update (or .dfu file is chosen from the advanced menu), it will follow the process exactly as the Android GAIA version does, streaming the file and following GAIA protocol. Once done, it will prompt the user with either an error, or a success, and the user closes the windows app (deadend).
