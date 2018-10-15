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
    private int Port=4352;      
    private int BufferSize=1000;

    public bool PJLinkConnected;
    public bool PJLinkPolling = true;   // polling enabled by default
    public bool PJLinkDebugEnabled = true;     // debug messages enabled by default
        
    // polling timers
    private CTimer PollPowerTimer;
    private CTimer PollSourceTimer;
    private CTimer PollLampHoursTimer;

    // command strings
    private string pollPower = "%1POWR ?\r";
    private string pollSource = "%1INPT ?\r";
    private string pollLamp = "LAMP?";
    private string powerOn = "%1POWR 1\r";
    private string powerOff = "%1POWR 0\r";
    private string sourceChange = "%1INPT "; // XX

    public string currentSource;
    public uint lampHours;

    // feedback strings
    public const string projOff = "0";
    public const string projOn = "1";
    public const string projWarming = "3";
    public const string projCooling = "2";
    public string PJLinkPowerState;


    private void Debug(string debugMessage)
    {
        if (PJLinkDebugEnabled)
        {
            CrestronConsole.PrintLine("[PJLINK PROJECTOR] " + debugMessage);
        }
    }

    #endregion


    #region public methods    
    

    public void PJLinkInitialise(string IP)
    {
        IPaddress = IP;        
        Debug("Initialising Client for PJLink Projector @ " + IPaddress);
        
        Client = new TCPClient(IPaddress, Port, BufferSize);
        Client.SocketStatusChange += new TCPClientSocketStatusChangeEventHandler(PJLinkSocketStatusChange);
        Client.ConnectToServerAsync(PJLinkConnectCallBack);
        
        PollPowerTimer = new CTimer(PJLinkPollPower, null, 3000, 3000);          //start 3 second timer to check power state
        PollSourceTimer = new CTimer(PJLinkPollSource, null, 20000, 20000);      //start 20 second timer to check current source
        PollLampHoursTimer = new CTimer(PJLinkPollLamp, null, 300000, 300000);   //start 30 minute second timer to check lamp hours  
    }

   
    // method to enable or disable polling
    public void PJLinkPollStatus(bool poll)
    {
        if (poll)
        {
            Debug("Polling Enabled");
            PJLinkPolling = true;
        }
        else
        {
            Debug("Polling Disabled");
            PJLinkPolling = false;
        }
    }


    // method to turn projector on/off
    public void PJLinkPower(bool power)
    {
        if (power)
           PJLinkSendDataToServer(powerOn);
        else
           PJLinkSendDataToServer(powerOff);
    }


    // method to change projector source input
    public void PJLinkInputChange(string source)
    {
        PJLinkSendDataToServer(String.Concat(sourceChange," ",source,"\r"));
    }
    

    #endregion
        

    #region device polling
    

    private void PJLinkPollPower(object notUsed)
    {
        if (PJLinkPolling)
            PJLinkSendDataToServer(pollPower);
    }


    private void PJLinkPollSource(object notUsed)
    {
        if (PJLinkPolling)
            PJLinkSendDataToServer(pollSource);
    }


    private void PJLinkPollLamp(object notUsed)
    {
        if (PJLinkPolling)
            PJLinkSendDataToServer(String.Concat(pollLamp, " \r"));
    }


    #endregion


    #region client management


    // client connect callback method
    void PJLinkConnectCallBack(TCPClient Client)
    {
        if (Client.ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED)
        {
            Client.ReceiveDataAsync(PJLinkReceiveDataCallback); // set up callback when data received
        }
        else
        {
            CrestronEnvironment.Sleep(2000);
            Client.ConnectToServerAsync(PJLinkConnectCallBack);  // connection failed, try again
        }
    }


    // method to handle client socket status 
    void PJLinkSocketStatusChange(TCPClient myTCPClient, SocketStatus clientSocketStatus)
    {
        Debug(String.Format("[PJLink Projector] LAN client ({1}) reports: {0}", clientSocketStatus, IPaddress));

        if (clientSocketStatus == SocketStatus.SOCKET_STATUS_CONNECTED)
        {
            CrestronEnvironment.Sleep(2000);
            PJLinkConnected = true;
            Debug("SOCKET CONNECTED!");
        }
        else
        {
            PJLinkConnected = false;
            CrestronEnvironment.Sleep(2000); //attempt reconnect after 2 seconds
            Debug("SOCKET DISCONNECTED!");
            Client.ConnectToServerAsync(PJLinkConnectCallBack);
        }
    }


    // client data received callback method
    void PJLinkReceiveDataCallback(TCPClient client, int QtyBytesReceived)
    {
        if (QtyBytesReceived > 0)
        {
            string dataReceived = Encoding.Default.GetString(Client.IncomingDataBuffer, 0, QtyBytesReceived);
            PJLinkOnDataReceive(dataReceived);
            client.ReceiveDataAsync(PJLinkReceiveDataCallback);
        }
        else
        {
            if (client.ClientStatus != SocketStatus.SOCKET_STATUS_CONNECTED)
            {
                Client.ConnectToServerAsync(PJLinkConnectCallBack);  // connection dropped, try again!
            }
        }
    }


    void PJLinkOnDataReceive(string data)
    {
        //Debug("data received = " + data);

        if (data.Contains("%1POWR=0"))
        {
            PJLinkPowerState = projOff;
            Debug("Projector reports power off");
        }

        if (data.Contains("%1POWR=1"))
        {
            PJLinkPowerState = projOn;
            Debug("Projector reports power on");
        }

        if (data.Contains("%1POWR=2"))
        {
            PJLinkPowerState = projCooling;
            Debug("Projector reports cooling down");
        }

        if (data.Contains("%1POWR=3")) 
        {
            PJLinkPowerState = projWarming;
            Debug("Projector reports warming up");
        }

        if (data.Contains("%1POWR=ERR3"))  //  unavailable time 
        {
            // nb: epson projector responds err3 when warming up
            PJLinkPowerState = projWarming;
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


    void PJLinkSendDataToServer(string cmd)
    {
        Debug("data sent: " + cmd);
        Client.SendData(Encoding.ASCII.GetBytes(cmd), cmd.Length);            
    }

    #endregion
    

}



