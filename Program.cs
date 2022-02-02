using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Threading;
using Communication.HartLite;
using Newtonsoft.Json;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

namespace HartUFMG
{

    class Programm
    {
        MqttClient mqttClient;
        Dictionary<byte, string> UnityCode = new Dictionary<byte, string>();

        public void Teste()
        {
            CreateUnityDictionary();
            string[] ports = SerialPort.GetPortNames();

            // Display each port name to the console.
            foreach (string port in ports)
            {
                Console.WriteLine(port);
            }

            HartCommunicationLite hartCommunicationLite = new HartCommunicationLite("COM1");
            OpenResult openResult = hartCommunicationLite.Open();
            Console.WriteLine("Conectando se porta COM1");
            if (openResult != OpenResult.Opened)
                return;
            Console.WriteLine("Conectando a porta COM1!");
            hartCommunicationLite.PreambleLength = 10;
            hartCommunicationLite.Receive += ReceiveValueHandle;
            hartCommunicationLite.SendingCommand += SendingValueHandle;


            
            try
            {
                /*
                mqttClient = new MqttClient("9ad5964d48f54c5d90d18fbec9bb78d8.s2.eu.hivemq.cloud",
                                uPLibrary.Networking.M2Mqtt.MqttSettings.MQTT_BROKER_DEFAULT_SSL_PORT, true, MqttSslProtocols.TLSv1_2, null, null);
                //.ProtocolVersion = MqttProtocolVersion.Version_3_1;*/
                mqttClient = new MqttClient("127.0.0.1");
                Console.WriteLine("Conectando se ao Broker em 127.0.0.1:8086...");
                mqttClient.MqttMsgPublishReceived += MqttClient_MqttMsgPublishReceived1;
               // mqttClient.Subscribe(new string[] { "devices/sensors/pressure" }, new byte[] { MqttMsgBase.QOS_LEVEL_AT_MOST_ONCE });
                mqttClient.Connect(Guid.NewGuid().ToString(), "MyDevice", "MyDevice123");
                Console.WriteLine("Conexão ao Broker em 127.0.0.1:8086 bem sucedida!");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return;
            }


            while (true)
            {
                //input should be in format like "1:0:0:0:0:0:0:0:0:0:0:0:0:0:0:0:0:0:0:0:0:0:0:0:0" 
                //this is the command 17 with 24 bytes with 0 value
                string input = "1:0:0:0:0:0:0:0:0:0:0:0:0:0:0:0:0:0:0:0:0:0:0:0:0";

                if (input == "exit")
                    break;

                //this part works only if the input is correct	
                string[] values = input.Split(new[] { ':' });
                List<byte> databytes = new List<byte>();

                for (int i = 1; i < values.Length; i++)
                {
                    //Error if some data not in a byte format!
                    databytes.Add(Convert.ToByte(values[i]));
                }

                hartCommunicationLite.SendAsync(Convert.ToByte(values[0]), databytes.ToArray());

                Thread.Sleep(5000);
            }

            hartCommunicationLite.Receive -= ReceiveValueHandle;
            hartCommunicationLite.SendingCommand -= SendingValueHandle;
        }

        private void SendingValueHandle(object sender, CommandRequest args)
        {
            Console.WriteLine(string.Format("Sended: {0} {1} {2} {3} {4} {5}", args.PreambleLength, args.Delimiter,
                        BitConverter.ToString(args.Address.ToByteArray()), args.CommandNumber, args.Data, args.Checksum));
        }

        private void ReceiveValueHandle(object sender, CommandResult args)
        {
            if(args.CommandNumber == 0x01)
            {
                byte[] bytes = new byte[args.Data.Length - 1];
                //unity bytes: mbar, N, psi etc...
                byte unityByte = new byte();
                unityByte = args.Data[0];
                Console.WriteLine(unityByte.ToString());
                for (int i = 1; i < args.Data.Length; i++)
                {
                    bytes[i - 1] = args.Data[i];
                }
                var hexString = BitConverter.ToString(bytes).Replace("-", "");
                uint num = uint.Parse(hexString, System.Globalization.NumberStyles.AllowHexSpecifier);

                byte[] floatVals = BitConverter.GetBytes(num);
                float f = BitConverter.ToSingle(floatVals, 0);


                var value = Convert.ToInt32(hexString, 16);
                //var value = BitConverter.ToDouble(args.Data, 1);
                Console.WriteLine(string.Format("Recebeu: Preâmbulo [{0}] Delimitador [{1}] Endereço [{2}] Comando [{3}] PV [{4}] Código resposta [{5}] CheckSum [{6}]", args.PreambleLength, args.Delimiter,
                            BitConverter.ToString(args.Address.ToByteArray()), args.CommandNumber, f.ToString(),
                            BitConverter.ToString(new[] { args.ResponseCode.FirstByte, args.ResponseCode.SecondByte }),
                           args.Checksum));

                SendToCloud(args, f, unityByte);
            }
            else
            {
                Console.WriteLine(string.Format("Recebeu: Preâmbulo [{0}] Delimitador [{1}] Endereço [{2}] Comando [{3}] {4} Código resposta [{5}] CheckSum [{6}]", args.PreambleLength, args.Delimiter,
                            BitConverter.ToString(args.Address.ToByteArray()), args.CommandNumber, BitConverter.ToString(args.Data),
                            BitConverter.ToString(new[] { args.ResponseCode.FirstByte, args.ResponseCode.SecondByte }),
                           args.Checksum));

            }
        }

        private void SendToCloud(CommandResult args, float f, byte b)
        {
            string jsonP = JsonConvert.SerializeObject(new
            {
                Telemetry = f,
                Unit = UnityCode[b],
                QoS = "Good",
                sent = DateTime.Now
            });
            if (mqttClient != null && mqttClient.IsConnected)
            {
                mqttClient.Publish("devices/sensors/pressure", Encoding.UTF8.GetBytes(jsonP));
                Console.WriteLine(string.Format("Mensagem enviada ao tópico devices/sensors/pressure:\nTelemetria: {0} {1} \n", f, UnityCode[b]));
            }


        }

        private static void MqttClient_MqttMsgPublishReceived1(object sender, uPLibrary.Networking.M2Mqtt.Messages.MqttMsgPublishEventArgs e)
        {
            var message = Encoding.UTF8.GetString(e.Message);
            Console.WriteLine("Mensagem recebida: " + message);
        }

        public static void Main()
        {
            Programm teste = new Programm();
            teste.Teste();

        }

        public void CreateUnityDictionary()
        {
            UnityCode.Add(0x06, "psi");
            UnityCode.Add(0x07, "bar");
            UnityCode.Add(0x08, "mbar");
            UnityCode.Add(0x09, "g/cm²");
            UnityCode.Add(0x0A, "kg/cm²");
            UnityCode.Add(0x0B, "Pa");
            UnityCode.Add(0x0C, "kPa");
            UnityCode.Add(0x0D, "torr");
            UnityCode.Add(0x0E, "MPa");
            UnityCode.Add(0x0F, "gal/sec");

            UnityCode.Add(0x10, "gal/min");
            UnityCode.Add(0x11, "gal/hr");
            UnityCode.Add(0x12, "l/sec");
            UnityCode.Add(0x13, "l/min");
            UnityCode.Add(0x14, "l/hr");
            UnityCode.Add(0x15, "m³/sec");
            UnityCode.Add(0x16, "m³/min");
            UnityCode.Add(0x17, "m³/hr");
            UnityCode.Add(0x18, "ft³/sec");
            UnityCode.Add(0x19, "ft³/min");
            UnityCode.Add(0x1A, "ft³/hr");
            UnityCode.Add(0x1B, "g/sec");
            UnityCode.Add(0x1C, "g/min");
            UnityCode.Add(0x1D, "g/h");
            UnityCode.Add(0x1F, "Kg/sec");

            UnityCode.Add(0x20, "Kg/min");
            UnityCode.Add(0x21, "Kg/hr");
            UnityCode.Add(0x22, "lb/sec");
            UnityCode.Add(0x23, "lb/min");
            UnityCode.Add(0x24, "lb/hr");
            UnityCode.Add(0x25, "°C");
            UnityCode.Add(0x26, "°F");
            UnityCode.Add(0x27, "°Rad");
            UnityCode.Add(0x28, "Kelvin");
            UnityCode.Add(0x29, "ft/sec");
            UnityCode.Add(0x2A, "m/sec");
            UnityCode.Add(0x2B, "in/sec");
            UnityCode.Add(0x2C, "in/min");
            UnityCode.Add(0x2D, "ft/min");
            UnityCode.Add(0x2E, "m/hr");
            UnityCode.Add(0x2F, "gal");

            UnityCode.Add(0x30, "liter");
            UnityCode.Add(0x31, "m³");
            UnityCode.Add(0x32, "bbl");
            UnityCode.Add(0x33, "yd³");
            UnityCode.Add(0x34, "ft³");
            UnityCode.Add(0x35, "in³");
            UnityCode.Add(0x36, "ft");
            UnityCode.Add(0x37, "m");
            UnityCode.Add(0x38, "cm");
            UnityCode.Add(0x39, "mm");
            UnityCode.Add(0x3A, "min");
            UnityCode.Add(0x3B, "sec");
            UnityCode.Add(0x3C, "hour");
            UnityCode.Add(0x3D, "day");
            UnityCode.Add(0x3E, "gram");
            UnityCode.Add(0x3F, "kg");

            UnityCode.Add(0x40, "lb");
            UnityCode.Add(0x41, "SGU");
            UnityCode.Add(0x42, "g/cm³");
            UnityCode.Add(0x43, "kg/m³");
            UnityCode.Add(0x44, "lb/gal");
            UnityCode.Add(0x45, "lb/ft³");
            UnityCode.Add(0x46, "g/ml");
            UnityCode.Add(0x47, "kg/l");
            UnityCode.Add(0x48, "g/l");
            UnityCode.Add(0x49, "cSt");
            UnityCode.Add(0x4A, "cpoise");
            UnityCode.Add(0x4B, "mV");
            UnityCode.Add(0x4C, "V");
            UnityCode.Add(0x4D, "mA");
            UnityCode.Add(0x4E, "Ohm");
            UnityCode.Add(0x4F, "kOhm");

            UnityCode.Add(0x50, "N_m");
            UnityCode.Add(0x51, "daTherm");
            UnityCode.Add(0x52, "ft_lbf");
            UnityCode.Add(0x53, "kWh");
            UnityCode.Add(0x54, "Mcal");
            UnityCode.Add(0x55, "MJ");
            UnityCode.Add(0x56, "Btu");
            UnityCode.Add(0x57, "kW");
            UnityCode.Add(0x58, "hp");
            UnityCode.Add(0x59, "Mcal/hr");
            UnityCode.Add(0x5A, "MJ/hr");
            UnityCode.Add(0x5B, "Btu/hr");
            UnityCode.Add(0x5C, "deg/sec");
            UnityCode.Add(0x5D, "rev/sec");
            UnityCode.Add(0x5E, "rpm");
            UnityCode.Add(0x5F, "Hz");

            UnityCode.Add(0x60, "%");
            UnityCode.Add(0x61, "pH");
            UnityCode.Add(0x62, "N");
            UnityCode.Add(0x63, "ppm");
            UnityCode.Add(0x64, "deg");
            UnityCode.Add(0x65, "rad");
            UnityCode.Add(0x66, "pF");
        }
    }

}
