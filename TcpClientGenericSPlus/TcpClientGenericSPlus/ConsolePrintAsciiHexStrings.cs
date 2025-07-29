using System.Data.SqlClient;
using Crestron.SimplSharp;
using System.Text;
    
namespace TcpClientGenericSPlus
{
    public class ConsolePrintAsciiHexStrings
    {
        public static void Print(string str, string prepend)
        {
            var sb = new StringBuilder();
            if(prepend.Length > 0)
                sb.Append(prepend);
            foreach (byte num in str)
            {
                if (num < 32 || num > 127)
                    sb.Append("\\x" + num.ToString("X2"));
                else
                    sb.Append((char) num);
            }
            CrestronConsole.PrintLine("{0}\r", sb);
        }

        public static void Print(string str)
        {
            if (str.Length <= 0)
                return;
            Print(str, "");
        }

    }
}