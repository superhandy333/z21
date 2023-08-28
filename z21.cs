﻿/* Z21 - C#-Implementierung des Protokolls der Kommunikation mit der digitalen
 * Steuerzentrale Z21 oder z21 von Fleischmann/Roco
 * ---------------------------------------------------------------------------
 * Datei:     z21.cs
 * Version:   16.06.2014 - Neu (Protokollversion 1.03)
 * Version:   20.08.2023 - Umstellung UdpClient
 * Version:   27.08.2023 - Protokollversion 1.12
 * Besitzer:  Mathias Rentsch (rentsch@online.de)
 * Lizenz:    GPL
 *
 * Die Anwendung und die Quelltextdateien sind freie Software und stehen unter der
 * GNU General Public License. Der Originaltext dieser Lizenz kann eingesehen werden
 * unter http://www.gnu.org/licenses/gpl.html.
 * 
 */
                 
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LokPower
{
    public class Z21StartData
    {
        public string LanAdresse = string.Empty;
        public int LanPort;
    }

    public enum HardwareTyp
    { 
        Z21_OLD,
        Z21_NEW,
        SMARTRAIL,
        z21_SMALL,
        z21_START,
        SINGLE_BOOSTER,
        DUAL_BOOSTER,
        Z21_XL,
        XL_BOOSTER,
        Z21_SWITCH_DECODER,
        Z21_SIGNAL_DECODER,
        None
    };

    public class HardwareInfo
    {
        public HardwareTyp Hardware;
        public FirmwareVersionInfo FirmwareVersion;
        public HardwareInfo(HardwareTyp hardware, FirmwareVersionInfo firmware)
        {
            Hardware = hardware;
            FirmwareVersion = firmware; 
        }
    }

    public enum VersionTyp { Z21, z21, Other, None};

    public class VersionInfo
    {
        public int XBusVersion;     // XBUS_VER
        public VersionTyp Version;
        public int CMDST_ID;        // CMDST_ID
        public VersionInfo(int xBusVersion, VersionTyp version,int cmdst_id)
        {
            XBusVersion = xBusVersion;
            Version = version;
            CMDST_ID = cmdst_id;
        }
    } 

    public class FirmwareVersionInfo
    {
        public int Major;
        public int Minor;
        public FirmwareVersionInfo(int major, int minor)
        {
            Major = major;
            Minor = minor;
        }
        public new string ToString()
        {
            return Major.ToString("X")+"."+Minor.ToString("X");    // z21 liefert minor hex
        }
    }

    public class LokInfoData
    {
        public LokAdresse Adresse;
        public bool Besetzt;
        public RichtungsAngabe Richtung;
        public byte Fahrstufe;
        public LokInfoData()
        {
            Adresse = new LokAdresse();
        }
    }

    public class CentralStateData
    {
        public bool EmergencyStop = true;
        public bool TrackVoltageOff = true;
        public bool ShortCircuit = true;
        public bool ProgrammingModeActive = true;
    }

    public class CentralStateDataEx
    {
        public bool HighTemperature = true;
        public bool PowerLost = true;
        public bool ShortCircuitExternal = true;
        public bool ShortCircuitInternal = true;
    }

    public class SystemStateData
    {
        public int MainCurrent = -1;
        public int ProgCurrent = -1;
        public int FilteredMainCurrent = -1;
        public int Temperature = -1;
        public int SupplyVoltage = -1;
        public int VCCVoltage = -1;
        public CentralStateData CentralState;
        public CentralStateDataEx CentralStateEx;
        public SystemStateData()
        {
            CentralState = new CentralStateData();
            CentralStateEx = new CentralStateDataEx();
        }
    }

    public class Z21
    {
        private readonly UdpClient udpClient;
        public Z21(Z21StartData startData)
        {
            udpClient = new UdpClient(startData.LanPort);
            lanAdresse = IPAddress.Parse(startData.LanAdresse);
            lanAdresseS = startData.LanAdresse;
            lanPort = startData.LanPort;
            udpClient.Connect(lanAdresse, lanPort);
            udpClient.DontFragment = false;
            udpClient.EnableBroadcast = false;
            udpClient.BeginReceive(new AsyncCallback(empfang), null);
            Console.WriteLine("Z21 initialisiert.");
        }

        private IPAddress lanAdresse;
        private string lanAdresseS;

        public string LanAdresse
        {
            get
            {
              return lanAdresseS;
            }
        }

        private int lanPort;
        public int LanPort
        {
            get
            {
              return lanPort;          
            }
        }

        public event EventHandler<DataEventArgs> OnReceive;                         //  Allgemeiner Empfang von Daten
        public event EventHandler<GetSerialNumberEventArgs> OnGetSerialNumber;      //  10    LAN GET SERIAL NUMBER  2.1 (10)  
        public event EventHandler<VersionInfoEventArgs> OnGetVersion;               //  40 21 LAN X GET VERSION  2.3 (xx)
        public event EventHandler OnTrackPowerOFF;                                  //  40 61 LAN X BC TRACK POWER OFF 2.7 (12) 
        public event EventHandler OnTrackPowerON;                                   //  40 61 LAN X BC TRACK POWER ON  2.8 (12) 
        public event EventHandler OnProgrammingMode;                                //  40 61 LAN X BC PROGRAMMING MODE 2.9 (12) 
        public event EventHandler OnTrackShortCircuit;                              //  40 61 LAN X BC TRACK SHORT CIRCUIT 2.10 (12) 
        public event EventHandler<StateEventArgs> OnStatusChanged;                  //  40 62 LAN X STATUS CHANGED 2.12 (13)
        public event EventHandler OnStopped;                                        //  40 81 LAN X BC STOPPED 2.14 (14)
        public event EventHandler<FirmwareVersionInfoEventArgs> OnGetFirmwareVersion;// 40 F3 LAN X GET FIRMWARE VERSION 2.15 (xx)
        public event EventHandler<SystemStateEventArgs> OnSystemStateDataChanged;   //  84    LAN SYSTEMSTATE_DATACHANGED 2.18 (18)
        public event EventHandler<HardwareInfoEventArgs> OnGetHardwareInfo;         //  1A    LAN GET HWINFO 2.20 (19)
        public event EventHandler<GetLocoInfoEventArgs> OnGetLocoInfo;              //  40 EF LAN X LOCO INFO   4.4 (22)
        public event EventHandler<TrackPowerEventArgs> OnTrackPower;                //  ist Zusammenfassung von 
        

        private void empfang(IAsyncResult res)
        {
            try
            {
                IPEndPoint RemoteIpEndPoint = null;
                byte[] received = udpClient.EndReceive(res, ref RemoteIpEndPoint);
                udpClient.BeginReceive(new AsyncCallback(empfang), null);
                if (OnReceive != null) OnReceive(this, new DataEventArgs(received));
                cutTelegramm(received);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Fehler beim Empfang  " + ex.Message);
            }
        }

        private void endConnect(IAsyncResult res)
        {
            Console.WriteLine("Reconnection abgeschlossen");
            udpClient.Client.EndConnect(res);
        }

        private void cutTelegramm(byte[] bytes)
        {
            if (bytes == null) return;
            int z = 0;
            int length = 0;
            int max = bytes.GetLength(0);
            while (z < max)
            {
                length = bytes[z];
                if ((length > 3) & ((z + length) <= max))
                {
                    byte[] einzelbytes = new byte[length];
                    Array.Copy(bytes, z, einzelbytes, 0, length);
                    evaluation(einzelbytes);
                    z += length;
                }
                else
                {
                    z = max;  //Notausgang, falls ungültige Länge, Restliche Daten werden verworfen
                    Console.WriteLine("> Fehlerhaftes Telegramm.");
                }
            }
            
        }

        private void evaluation(byte[] received)
        {
            bool b;
            int i;

            switch (received[2])
            {
                case 0x1A:           //  LAN GET HWINFO  2.20 (19)
                    Console.WriteLine("> LAN GET HWINFO " + getByteString(received));
                    HardwareTyp hardwareTyp;
                    i = (received[7] << 24) + (received[6] << 16) + (received[5] << 8) + (received[4]);
                    switch (i)
                    {
                        case 0x00000200: hardwareTyp = HardwareTyp.Z21_OLD; break;
                        case 0x00000201: hardwareTyp = HardwareTyp.Z21_NEW; break;
                        case 0x00000202: hardwareTyp = HardwareTyp.SMARTRAIL; break;
                        case 0x00000203: hardwareTyp = HardwareTyp.z21_SMALL; break;
                        case 0x00000204: hardwareTyp = HardwareTyp.z21_START; break;
                        case 0x00000205: hardwareTyp = HardwareTyp.SINGLE_BOOSTER; break;
                        case 0x00000206: hardwareTyp = HardwareTyp.DUAL_BOOSTER; break;
                        case 0x00000211: hardwareTyp = HardwareTyp.Z21_XL; break;
                        case 0x00000212: hardwareTyp = HardwareTyp.XL_BOOSTER; break;
                        case 0x00000301: hardwareTyp = HardwareTyp.Z21_SWITCH_DECODER; break;
                        case 0x00000302: hardwareTyp = HardwareTyp.Z21_SIGNAL_DECODER; break;
                        default: hardwareTyp = HardwareTyp.None; break;
                    }
                    FirmwareVersionInfo firmware = new(received[9],received[8]);
                    if (OnGetHardwareInfo != null) OnGetHardwareInfo(this, new HardwareInfoEventArgs(new HardwareInfo(hardwareTyp, firmware)));
                    break;
                case 0x10:           //  LAN GET SERIAL NUMBER  2.1 (10)
                    Console.WriteLine("> LAN GET SERIAL NUMBER " + getByteString(received));
                    i = (received[7]<<24)+(received[6]<<16)+(received[5]<<8)+(received[4]);
                    if (OnGetSerialNumber != null) OnGetSerialNumber(this, new GetSerialNumberEventArgs(i));
                    
                    break;
                case 0x40:           //  X-Bus-Telegramm
                    switch (received[4])
                    {
                        case 0x61:           
                            switch (received[5])
                            {
                                case 0x00:           //  LAN X BC TRACK POWER OFF  2.7 (12)
                                    Console.WriteLine("> LAN X BC TRACK POWER OFF " + getByteString(received));
                                    if (OnTrackPowerOFF != null) OnTrackPowerOFF(this, new EventArgs());
                                    if (OnTrackPower != null) OnTrackPower(this, new TrackPowerEventArgs(false));
                                    break;
                                case 0x01:           //  LAN X BC TRACK POWER ON  2.8 (12)
                                    Console.WriteLine("> LAN X BC TRACK POWER ON " + getByteString(received));
                                    if (OnTrackPowerON != null) OnTrackPowerON(this, new EventArgs());
                                    if (OnTrackPower != null) OnTrackPower(this, new TrackPowerEventArgs(true));
                                    break;
                                case 0x02:           //  LAN X BC PROGRAMMING MODE  2.9 (12)
                                    Console.WriteLine("> LAN X BC PROGRAMMING MODE " + getByteString(received));
                                    if (OnProgrammingMode!= null) OnProgrammingMode(this, new EventArgs());
                                    break;
                                case 0x08:           //  LAN X BC TRACK SHORT CIRCUIT  2.10 (12)
                                    Console.WriteLine("> LAN X BC TRACK SHORT CIRCUIT " + getByteString(received));                                    
                                    if (OnTrackShortCircuit != null) OnTrackShortCircuit(this, new EventArgs());
                                    break;
                                default:
                                    Console.WriteLine("> Unbekanntes X-Bus-Telegramm Header 61" + getByteString(received));
                                    break;
                            }
                            break;
                        case 0x62:           //  LAN X STATUS CHANGED  2.12 (13)
                            Console.WriteLine("> LAN X STATUS CHANGED " + getByteString(received));
                            CentralStateData centralStateData = getCentralStateData(received);
                            if (OnStatusChanged != null) OnStatusChanged(this, new StateEventArgs(centralStateData));
                            break;
                        case 0x63:
                            switch (received[5])
                            {
                                case 0x21:           //  LAN X GET VERSION  2.3 (10)
                                    Console.WriteLine("> LAN X GET VERSION " + getByteString(received));
                                    VersionTyp versionTyp;
                                    switch (received[7])
                                    {
                                        case 0x00:
                                            versionTyp = VersionTyp.None;
                                            break;
                                        case 0x12:
                                            versionTyp = VersionTyp.Z21;
                                            break;
                                        case 0x13:
                                            versionTyp = VersionTyp.z21;  // 0x13 ist keine gesicherte Erkenntnis aus dem LAN-Protokoll, wird aber von meiner z21 so praktiziert
                                            break;
                                        default:
                                            versionTyp = VersionTyp.Other;
                                            break;
                                    }
                                    if (OnGetVersion != null) OnGetVersion(this, new VersionInfoEventArgs(new VersionInfo(received[6], versionTyp,received[7])));
                                    break;
                                default:
                                    Console.WriteLine("> Unbekanntes X-Bus-Telegramm Header 63" + getByteString(received));
                                    break;
                            }
                            break;
                        
                        case 0x81:           //  LAN X BC STOPPED  2.14 (14)
                            Console.WriteLine("> LAN X BC STOPPED " + getByteString(received));                                    
                            if (OnStopped != null) OnStopped(this, new EventArgs());
                            break;
                        case 0xEF:           //  LAN X LOCO INFO  4.4 (22)
                            
                            ValueBytesStruct vbs = new ValueBytesStruct();
                            vbs.Adr_MSB = received[5];
                            vbs.Adr_LSB = received[6];
                            LokInfoData infodata = new LokInfoData();
                            infodata.Adresse = new LokAdresse(vbs);
                            infodata.Besetzt = ((received[7] & 8) == 8);
                            infodata.Fahrstufe = (byte)(received[8] & 0x7F);
                            b = ((received[8] & 0x80) == 0x80);
                            if (b) infodata.Richtung = RichtungsAngabe.Forward; else infodata.Richtung = RichtungsAngabe.Backward;
                            Console.WriteLine("> LAN X LOCO INFO " + getByteString(received) + " (#" + infodata.Adresse+" - "+infodata.Fahrstufe.ToString() + ")");
                            if (OnGetLocoInfo != null) OnGetLocoInfo(this, new GetLocoInfoEventArgs(infodata));
                            
                            break;
                        case 0xF3:   
                            switch (received[5])
                            {
                                case 0x0A:           //  LAN X GET FIRMWARE VERSION 2.15 (xx)
                                    Console.WriteLine("> LAN X GET FIRMWARE VERSION " + getByteString(received));
                                    if (OnGetFirmwareVersion!= null) OnGetFirmwareVersion(this, new FirmwareVersionInfoEventArgs(new FirmwareVersionInfo(received[6],received[7])));
                                    // Achtung: die z21 bringt die Minor-Angabe hexadezimal !!!!!!!!    z.B. Firmware 1.23 = Minor 34
                                    break;
                                default:
                                    Console.WriteLine("> Unbekanntes X-Bus-Telegramm Header F3" + getByteString(received));
                                break;
                            }
                            break;
                        default:
                            Console.WriteLine("> Unbekanntes X-Bus-Telegramm " + getByteString(received));
                            break;
                    }
                    break;
                case 0x84:            // LAN SYSTEMSTATE DATACHANGED    2.18 (16)
                    Console.WriteLine("> LAN SYSTEMSTATE DATACHANGED " + getByteString(received));
                    SystemStateData systemStateData = getSystemStateData(received);
                    if (OnSystemStateDataChanged != null) OnSystemStateDataChanged(this, new SystemStateEventArgs(systemStateData));
                    
                    break;
                default: 
                    Console.WriteLine("> Unbekanntes Telegramm " + getByteString(received));
                    break;
            }
        }


        private CentralStateData getCentralStateData(byte[] received)
        {
            CentralStateData statedata = new()
            {
                EmergencyStop = ((received[6] & 0x01) == 0x01),
                TrackVoltageOff = ((received[6] & 0x02) == 0x02),
                ShortCircuit = ((received[6] & 0x04) == 0x04),
                ProgrammingModeActive = ((received[6] & 0x20) == 0x20)
            };
            return statedata;
        }

        private SystemStateData getSystemStateData(byte[] received)
        {
            SystemStateData statedata = new()
            {
                MainCurrent = (received[5] << 8) + (received[4]),
                ProgCurrent = (received[7] << 8) + (received[6]),
                FilteredMainCurrent = (received[9] << 8) + (received[8]),
                Temperature = (received[11] << 8) + (received[10]),
                SupplyVoltage = (received[13] << 8) + (received[12]),
                VCCVoltage = (received[15] << 8) + (received[14])
            };
            statedata.CentralState.EmergencyStop = ((received[16] & 0x01) == 0x01);
            statedata.CentralState.TrackVoltageOff = ((received[16] & 0x02) == 0x02);
            statedata.CentralState.ShortCircuit = ((received[16] & 0x04) == 0x04);
            statedata.CentralState.ProgrammingModeActive = ((received[16] & 0x20) == 0x20);

            statedata.CentralStateEx.HighTemperature = ((received[17] & 0x01) == 0x01);
            statedata.CentralStateEx.PowerLost = ((received[17] & 0x02) == 0x02);
            statedata.CentralStateEx.ShortCircuitExternal = ((received[17] & 0x04) == 0x04);
            statedata.CentralStateEx.ShortCircuitInternal = ((received[17] & 0x08) == 0x08);
            return statedata;
        }
 
        //  LAN_GET_SERIAL_NUMBER()     // 2.1 (10)
        public void GetSerialNumber()
        {
            byte[] bytes = new byte[4];
            bytes[0] = 0x04;
            bytes[1] = 0;
            bytes[2] = 0x10;
            bytes[3] = 0;
            Console.WriteLine("LAN GET SERIAL NUMBER " + getByteString(bytes));
            Senden(bytes);
        }

        //  LAN_X_GET_VERSION     // 2.3 (10)
        public void GetVersion()
        {
            byte[] bytes = new byte[7];
            bytes[0] = 0x07;
            bytes[1] = 0;
            bytes[2] = 0x40;
            bytes[3] = 0;
            bytes[4] = 0x21;
            bytes[5] = 0x21;
            bytes[6] = 0;
            Console.WriteLine("LAN X GET VERSION " + getByteString(bytes));
            Senden(bytes);
        }
        
        //  LAN_X_GET_STATUS     // 2.4 (11)
        public void GetStatus()          
        {
            byte[] bytes = new byte[7];
            bytes[0] = 0x07;
            bytes[1] = 0;
            bytes[2] = 0x40;
            bytes[3] = 0;
            bytes[4] = 0x21;
            bytes[5] = 0x24;
            bytes[6] = 0x05;   // = XOR-Byte
            Console.WriteLine("LAN X GET STATUS " + getByteString(bytes));
            Senden(bytes);
        }

        //  LAN_X_SET_TRACK_POWER_OFF   // 2.5 (11)
        public void SetTrackPowerOFF()
        {
            byte[] bytes = new byte[7];
            bytes[0] = 0x07;
            bytes[1] = 0;
            bytes[2] = 0x40;
            bytes[3] = 0;
            bytes[4] = 0x21;
            bytes[5] = 0x80;
            bytes[6] = 0xA1;   // = XOR-Byte
            Console.WriteLine("LAN X SET TRACK POWER OFF " + getByteString(bytes));
            Senden(bytes);
        }

        //  LAN_X_SET_TRACK_POWER_ON   // 2.6 (11)
        public void SetTrackPowerON()
        {
            byte[] bytes = new byte[7];
            bytes[0] = 0x07;
            bytes[1] = 0;
            bytes[2] = 0x40;
            bytes[3] = 0;
            bytes[4] = 0x21;
            bytes[5] = 0x81;
            bytes[6] = 0xA0;   // = XOR-Byte
            Console.WriteLine("LAN X SET TRACK POWER OFF " + getByteString(bytes));
            Senden(bytes);
        }

        //  LAN_X_SET_STOP   // 2.13 (14)
        public void SetStop()
        {
            byte[] bytes = new byte[6];
            bytes[0] = 0x06;
            bytes[1] = 0;
            bytes[2] = 0x40;
            bytes[3] = 0;
            bytes[4] = 0x80;
            bytes[5] = 0x80;   // = XOR-Byte
            Console.WriteLine("LAN X SET STOP " + getByteString(bytes));
            Senden(bytes);
        }

        //  LAN_X_GET_FIRMWARE_VERSION   // 2.15 (xx)
        public void GetFirmwareVersion()
        {
            byte[] bytes = new byte[7];
            bytes[0] = 0x07;
            bytes[1] = 0;
            bytes[2] = 0x40;
            bytes[3] = 0;
            bytes[4] = 0xF1;
            bytes[5] = 0x0A;
            bytes[6] = 0xFB;   // = XOR-Byte
            Console.WriteLine("LAN X GET FIRMWARE VERSION " + getByteString(bytes));
            Senden(bytes);
        }

        //  LAN_SET_BROADCASTFLAGS()    // 2.16 (15)
        public void SetBroadcastFlags()
        {
            byte[] bytes = new byte[8];
            bytes[0] = 0x08;
            bytes[1] = 0;
            bytes[2] = 0x50;
            bytes[3] = 0;
            bytes[4] = 1;         //  0x0000001 Broadcast für Fahren/Schalten 
            bytes[5] = 1;         //  0x0000100 Broadcast für LAN_SYSTEMSTATE_DATACHANGED
            bytes[6] = 0;
            bytes[7] = 0;
            Console.WriteLine("LAN SET BROADCASTFLAGS " + getByteString(bytes));
            Senden(bytes);
        }

        //  LAN_SYSTEMSTATE_GETDATA()     // 2.19 (19)
        public void SystemStateGetData()
        {
            byte[] bytes = new byte[4];
            bytes[0] = 0x04;
            bytes[1] = 0;
            bytes[2] = 0x85;
            bytes[3] = 0;                              
            Console.WriteLine("LAN SYSTEMSTATE GETDATA " + getByteString(bytes));
            Senden(bytes);
        }

        //  LAN_GET_HWINFO   // 2.20 (xx)
        public void GetHardwareInfo()
        {
            byte[] bytes = new byte[4];
            bytes[0] = 0x04;
            bytes[1] = 0;
            bytes[2] = 0x1A;
            bytes[3] = 0;      // kein XOR-Byte  ???
            Console.WriteLine("LAN GET HWINFO " + getByteString(bytes));
            Senden(bytes);
        }
        
        //  LAN X GET LOCO INFO         // 4.1 (20)
        public void GetLocoInfo(LokAdresse adresse)   
        {
            if (adresse != null)
            {
                if (adresse.Value != 0)    // Adresse außerhalb des Wertebereiches bedeutet value=0
                {
                    byte[] bytes = new byte[9];
                    bytes[0] = 0x09;
                    bytes[1] = 0;
                    bytes[2] = 0x40;
                    bytes[3] = 0;
                    bytes[4] = 0xE3;
                    bytes[5] = 0xF0;
                    bytes[6] = adresse.ValueBytes.Adr_MSB;
                    bytes[7] = adresse.ValueBytes.Adr_LSB;
                    bytes[8] = (byte)(bytes[4] ^ bytes[5] ^ bytes[6] ^ bytes[7]);
                    Console.WriteLine("LAN X GET LOCO INFO " + getByteString(bytes) + " (#" + adresse.Value.ToString() + ")");
                    Senden(bytes);
                }
                else
                {
                    Console.WriteLine("GetLocoInfo: Ungültige LokAdresse (außerhalb Wertebereich)");
                }
            }
            else
            {
                Console.WriteLine("GetLocoInfo: Ungültige LokAdresse (null)");
            }
        }

        //  LAN_X_SET_LOCO_DRIVE  4.2  (21)
        public void SetLocoDrive(LokInfoData data)
        {
            if (data != null)
            {
                if (data.Adresse != null)
                {
                    if (data.Richtung == RichtungsAngabe.Forward) data.Fahrstufe |= 0x080;

                    byte[] bytes = new byte[10];
                    bytes[0] = 0x0A;
                    bytes[1] = 0;
                    bytes[2] = 0x40;
                    bytes[3] = 0;
                    bytes[4] = 0xE4;
                    bytes[5] = 0x13; //  = 128 Fahrstufen
                    bytes[6] = data.Adresse.ValueBytes.Adr_MSB;
                    bytes[7] = data.Adresse.ValueBytes.Adr_LSB;
                    bytes[8] = data.Fahrstufe;
                    bytes[9] = (byte)(bytes[4] ^ bytes[5] ^ bytes[6] ^ bytes[7] ^ bytes[8]);
                    Console.WriteLine("LAN X SET LOCO DRIVE " + getByteString(bytes) + "  (" + data.Adresse + " - " + data.Fahrstufe.ToString() + ")");
                    Senden(bytes);
                }
                else
                {
                    Console.WriteLine("SetLocoDrive: Ungültige LokAdresse (null)");
                }
            }
            else
            {
                Console.WriteLine("SetLocoDrive: Ungültige LokInfoData (null)");
            }
        }


        public void Nothalt()
        {
            SetTrackPowerOFF();  
        }
       
        //  LAN_LOGOFF            2.2 (10)         
        public void LogOFF()
        {
            byte[] bytes = new byte[4];
            bytes[0] = 0x04;
            bytes[1] = 0;
            bytes[2] = 0x30;
            bytes[3] = 0;
            Console.WriteLine("LAN LOGOFF " + getByteString(bytes));
            Senden(bytes);
            
        }

        private string getByteString(byte[] bytes)
        {
            string s = "";
            foreach (byte b in bytes)
            {
                s += b.ToString("X") + ",";
            }
            return s;
        }

        private void Senden(byte[] bytes)
        {
            try
            {
                udpClient.Send(bytes, bytes.GetLength(0));
            }
            catch (ArgumentNullException e)
            {
                Console.WriteLine("Fehler beim Senden. Zu sendende Bytes waren null.");
                Console.WriteLine(e.Message);
            }
            catch (ObjectDisposedException e)
            {
                Console.WriteLine("Fehler beim Senden. Der UdpClient ist geschlossen.");
                Console.WriteLine(e.Message);
            }
            catch (InvalidOperationException e)
            {
                Console.WriteLine("Fehler beim Senden. Der UdpClient hat bereits einen Standardremotehost eingerichtet.");
                Console.WriteLine(e.Message);
            }
            catch (SocketException e)
            {
                Console.WriteLine("Fehler beim Senden. Socket-Exception.");
                Console.WriteLine("Versuche es erneut.");
                udpClient.Client.BeginConnect(lanAdresse, lanPort, new AsyncCallback(endConnect), null);
                Console.WriteLine(e.Message);
                
            }
        }

        public void Reconnect()
        {
            try
            {
                udpClient.Client.BeginConnect(lanAdresse, lanPort, new AsyncCallback(endConnect), null);     
            }
            catch (Exception ex)
            {
                Console.WriteLine("Fehler beim Reconnection. "+ex.Message);   
            }
        }

        public void Dispose()
        {
            //LogOFF();
            udpClient.Close();
        }
    }

    public class GetLocoInfoEventArgs : EventArgs
    {
        public GetLocoInfoEventArgs(LokInfoData data)
            : base()
        {
            Data = data;
        }
        public LokInfoData Data;
    }

    public class GetSerialNumberEventArgs : EventArgs
    {
        public GetSerialNumberEventArgs(int serialNumber)
            : base()
        {
            SerialNumber = serialNumber;
        }
        public int SerialNumber;
    }

    public class StateEventArgs : EventArgs
    {
        public StateEventArgs(CentralStateData data)
            : base()
        {
            Data = data;
        }
        public CentralStateData Data;
    }

    public class SystemStateEventArgs : EventArgs
    {
        public SystemStateEventArgs(SystemStateData data)
            : base()
        {
            Data = data;
        }
        public SystemStateData Data;
    }

    public class DataEventArgs : EventArgs
    {
        public DataEventArgs(byte[] received)
            : base()
        {
            Received = received;
        }
        public byte[] Received;
    }

    public class TrackPowerEventArgs : EventArgs
    {
        public TrackPowerEventArgs(bool trackPowerOn)
            : base()
        {
            TrackPowerOn = trackPowerOn;
        }
        public bool TrackPowerOn;
    }

    public class HardwareInfoEventArgs : EventArgs
    {
        public HardwareInfoEventArgs(HardwareInfo data)
            : base()
        {
            Data = data;
        }
        public HardwareInfo Data;
    }

    public class VersionInfoEventArgs : EventArgs
    {
        public VersionInfoEventArgs(VersionInfo data)
            : base()
        {
            Data = data;
        }
        public VersionInfo Data;
    }

    public class FirmwareVersionInfoEventArgs : EventArgs
    {
        public FirmwareVersionInfoEventArgs(FirmwareVersionInfo data)
            : base()
        {
            Data = data;
        }
        public FirmwareVersionInfo Data;
    }

    public class LokAdresse
    {
        public LokAdresse()
        {
        }

        public LokAdresse(int adresse)
        {
            Value = adresse;
        }

        public LokAdresse(ValueBytesStruct valueBytes)
        {
            ValueBytes = valueBytes;
        }

        private int val = 0;
        public int Value
        {
            set
            {
                if ((value >= 1) & (value <= 9999))
                {
                    val = value;
                }
                else
                {
                    val = 0;
                }
            }
            get
            {
                return val;
            }
        }

        public ValueBytesStruct ValueBytes
        {
            set
            {
                Value = ((value.Adr_MSB & 0x3F) << 8) + value.Adr_LSB;
            }
            get
            {
                ValueBytesStruct vbs;

                try
                {
                    byte b = Convert.ToByte(val >> 8);
                    if (val >= 128)
                    {
                        b += 192;
                    }
                    vbs.Adr_MSB = b;
                    vbs.Adr_LSB = Convert.ToByte(val % 256);

                }
                catch
                {
                    vbs.Adr_MSB = 0;
                    vbs.Adr_LSB = 0;

                }
                return vbs;
            }
        }

        public override string ToString()
        {
            return Value.ToString();

        }

    }

    public enum RichtungsAngabe { Leerlauf, Forward, Backward };

    public struct ValueBytesStruct
    {
        public byte Adr_MSB;
        public byte Adr_LSB;
    }
}
