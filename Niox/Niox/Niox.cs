// NioxRs232Module.cs
// One-file SIMPL#Pro module for NXIO lighting over RS-232.
// - Core packet builder (NioxRs232)
// - S+-friendly wrapper surface (NioxRs232Splus) exposing primitives + SimplSharpString

using System;
using System.Runtime.InteropServices;
using Crestron.SimplSharp; // CTimer, SimplSharpString

namespace Niox
{
    public delegate void StringOut(SimplSharpString data);

 
    public class NioxClient
    {
        // ==== Outbound to S+ ====
        public StringOut TxOut { get; set; }      // raw frames to COM port (binary-safe)

        public void Initialize()
        {
        }

        public void Scene1()
        {
            SendScene(1);
        }
        public void Scene2()
        {
            SendScene(2);
        }
        public void Scene3()
        {
            SendScene(3);
        }
        public void Scene4()
        {
            SendScene(4);
        }

        public void SendScene(byte sceneIndex)
        {
            var frame = BuildSceneFrame(sceneIndex);
            if (TxOut != null)
            {
                var s = new SimplSharpString(BytesToRawString(frame, frame.Length));
                TxOut.Invoke(s);
            }

        }

        private static byte[] BuildSceneFrame(byte sceneIndex)
        {
            const byte SCENE_SUBJECT = 0x85;
            const int payLoadLength = 1;
            int len = 1 + 1 + 1 + payLoadLength + 2;
            var buf = new byte[len];
            buf[0] = 0xA5;
            buf[1] = (byte)len;
            buf[2] = SCENE_SUBJECT;
            buf[3] = sceneIndex;
            byte ck1 = 0x00, ck2 = 0x00;
            for (var i = 0; i < len - 2; i++)
            {
                if ((i & 1) == 0) ck1 ^= buf[i]; else ck2 ^= buf[i];
            }

            ck1 = (byte)~ck1;
            ck2 = (byte)~ck2;
            buf[len - 1] = ck1;
            buf[len - 2] = ck2;

            return buf;
        }

        private static string BytesToRawString(byte[] data, int count)
        {
            var chars = new char[count];
            for (int i = 0; i < count; i++)
            {
                chars[i] = (char)(data[i] & 0xFF);
                
            }
            return new string(chars);
        }
    }
}
