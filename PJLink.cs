using System;
using System.Text;
using Crestron.SimplSharp;                          	// For Basic SIMPL# Classes
using Crestron.SimplSharpPro;                       	// For Basic SIMPL#Pro classes
using Crestron.SimplSharpPro.Diagnostics;		    	// For System Monitor Access
using Crestron.SimplSharpPro.DeviceSupport;         	// For Generic Device Support
using Crestron.SimplSharp.CrestronIO;                   // For File/text operations
using Crestron.SimplSharp.CrestronSockets;              // For Ethernet socket connections


public class PJLink
{
    #region definitions

    private TCPClient Client;
    private string IPaddress;
    private const int Port=4352;      
    private const int BufferSize=1000;

    private bool disposed = false;

    public bool Connected {get; private set}

    private bool _polling;
    public bool Polling 
    {
        get; 
        set 
        {
            if (value)
            {
                Debug("Polling Enabled");
                _polling = true;
            }
            else
            {
                Debug("Polling Disabled");
                _polling = false;
            }
        }
    }
    public bool DebugEnable {get; set;}
        
    // polling timers
    private CTimer PollPowerTimer;
    private CTimer PollSourceTimer;
    private CTimer PollLampHoursTimer;

    // command strings
    private const string pollPower = "%1POWR ?\r";
    private const string pollSource = "%1INPT ?\r";
    private const string pollLamp = "LAMP ?";
    private const string powerOn = "%1POWR 1\r";
    private const string powerOff = "%1POWR 0\r";
    private const string sourceChange = "%1INPT "; // XX

    public string currentSource {get; private set;}
    public uint lampHours {get; private set;}
    private string friendlyName {get; set;}

    // feedback strings
    public const string projOff = "0";
    public const string projOn = "1";
    public const string projWarming = "3";
    public const string projCooling = "2";
    public string PowerState {get; set;}


    private void Debug(string msg)
    {
        if (DebugEnable)
        {
            //CrestronConsole.PrintLine("[PJLINK] " + debugMessage);
            CrestronConsole.PrintLine("[{0}]{1}",friendlyName, msg);
        }
    }

    #endregion


    #region public methods    
    
    
    public void Init(string IP)
    {
        IPaddress = IP;        
        Debug("Initialising Client for PJLink Projector @ " + IPaddress);
        
        Client = new TCPClient(IPaddress, Port, BufferSize);
        Client.SocketStatusChange += new TCPClientSocketStatusChangeEventHandler(SocketStatusChange);
        Client.ConnectToServerAsync(ConnectCallBack);
        
        PollPowerTimer = new CTimer(PollPower, null, 3000, 3000);          //start 3 second timer to check power state
        PollSourceTimer = new CTimer(PollSource, null, 20000, 20000);      //start 20 second timer to check current source
        PollLampHoursTimer = new CTimer(PollLamp, null, 300000, 300000);   //start 30 minute second timer to check lamp hours  
    }

    public void SetFriendlyName(string name)
    {
       friendlyName = name;
    }


    // method to turn projector on/off
    public void Power(bool power)
    {
        if (power)
           SendDataToServer(powerOn);
        else
           SendDataToServer(powerOff);
    }


    // method to change projector source input (refer to protocol document)
    public void InputChange(string source)
    {
        SendDataToServer(String.Concat(sourceChange," ",source,"\r"));
    }
    

    #endregion
        

    #region device polling
    

    private void PollPower(object notUsed)
    {
        if (Polling && Connected)
            SendDataToServer(pollPower);
    }


    private void PollSource(object notUsed)
    {
        if (Polling && Connected)
            SendDataToServer(pollSource);
    }


    private void PollLamp(object notUsed)
    {
        if (Polling && Connected)
            SendDataToServer(String.Concat(pollLamp, " \r"));
    }


    #endregion


    #region client management


    // client connect callback method
    void ConnectCallBack(TCPClient Client)
    {
        if (Client.ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED)
        {
            Client.ReceiveDataAsync(ReceiveDataCallback); // set up callback when data received
        }
        else
        {
            CrestronEnvironment.Sleep(2000);
            Client.ConnectToServerAsync(ConnectCallBack);  // connection failed, try again
            CrestronConsole.PrintLine("Attempting to reconnect PJLink Projector");
        }
    }


    // method to handle client socket status 
    void SocketStatusChange(TCPClient myTCPClient, SocketStatus clientSocketStatus)
    {
        Debug(String.Format("[PJLink Projector] LAN client ({1}) reports: {0}", clientSocketStatus, IPaddress));

        if (clientSocketStatus == SocketStatus.SOCKET_STATUS_CONNECTED)
        {
            CrestronEnvironment.Sleep(2000);
            Connected = true;
            Debug("SOCKET CONNECTED!");
        }
        else
        {
            Connected = false;
            CrestronEnvironment.Sleep(2000); //attempt reconnect after 2 seconds
            Client.ConnectToServerAsync(ConnectCallBack);
        }
    }


    // client data received callback method
    void ReceiveDataCallback(TCPClient client, int QtyBytesReceived)
    {
        if (QtyBytesReceived > 0)
        {
            string dataReceived = Encoding.Default.GetString(Client.IncomingDataBuffer, 0, QtyBytesReceived);
            OnDataReceive(dataReceived);
            client.ReceiveDataAsync(ReceiveDataCallback);
        }
        else
        {
            if (client.ClientStatus != SocketStatus.SOCKET_STATUS_CONNECTED)
            {
                Client.ConnectToServerAsync(ConnectCallBack);  // connection dropped, try again!
            }
        }
    }


    void OnDataReceive(string data)
    {
        //Debug("data received = " + data);

        if (data.Contains("%1POWR=0"))
        {
            PowerState = projOff;
            Debug("Projector reports power off");
        }

        if (data.Contains("%1POWR=1"))
        {
            PowerState = projOn;
            Debug("Projector reports power on");
        }

        if (data.Contains("%1POWR=2"))
        {
            PowerState = projCooling;
            Debug("Projector reports cooling down");
        }

        if (data.Contains("%1POWR=3")) 
        {
            PowerState = projWarming;
            Debug("Projector reports warming up");
        }

        if (data.Contains("%1POWR=ERR3"))  //  unavailable time 
        {
            // nb: epson projector responds err3 when warming up
            PowerState = projWarming;
            Debug("Projector reports unavailable");
        }

        if (data.Contains("%1POWR=ERR4"))
        {
            Debug("Projector reports projector failure!!!");
        }

        if (data.Contains("%1INPT=ERR2"))
        {
            Debug("Projector reports input does not exist!");
        }

        if (data.Contains("%1INPT="))
        {
            string sourceData = data;
            string temp;
            temp = sourceData.Remove(0, 7); // remove %1INPT=

            if (temp == "ERR3")
                Debug("Projector reports input unavailable");
            else
                Debug("Projector reports it is on source " + temp);
        }

        if (data.Contains("LAMP="))
        {
            string lampData = data;
            string temp;
            temp = lampData.Remove(0, 5);
            lampHours = Convert.ToUInt32(temp);
            Debug("Projector reports lamp hours = " + temp);
        }
    }


    void SendDataToServer(string cmd)
    {
        Debug("data sent: " + cmd);
        Client.SendData(Encoding.ASCII.GetBytes(cmd), cmd.Length);            
    }

    #endregion
    

}



