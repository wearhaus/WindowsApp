# WindowsApp
Code for Windows / Windows Phone app 

Instructions:
BEFORE RUNNING THE APP: Turn on Bluetooth through windows settings and pair with a Wearhaus Arc / device advertising the GAIA Service UUID
Build the Solution in Visual Studio, and hit the Run button in the App. Click Wearhaus Arc.

Files of Note (in C# project directory):
Shared/Scenario1_ChatClient.xaml.cs
Windows/MainPage.xaml.cs

Error details:
An exception is thrown from the "await RfcommDeviceService.FromIdAsync(chatServiceInfo.Id);" [Line 122, Scenario1_ChatClient.xaml.cs] (presumably) with the following additional information:

"A message sent on a datagram socket was larger than the internal message before or some other network limit, or the buffer used to receive a datagram into was smaller than the datagram itself. (Exception from HRESULT: 0x80072738)"
