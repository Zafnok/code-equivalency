using System;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;

namespace Equiv.Samples.RuntimeRowCallCannotReach
{
    public static class Calls
    {
        public static string NewProcessFile(string file)
        {
            var process = new Process { StartInfo = { FileName = file } };
            return process.StartInfo.FileName;
        }

        public static string ProcessFile(Process process) => process.StartInfo.FileName;

        public static string Katakana()
        {
            var bytes = new byte[3];
            bytes[0] = 0xE3;
            bytes[1] = 0x82;
            bytes[2] = 0xA2;
            return Encoding.UTF8.GetString(bytes);
        }

        public static string Truncated()
        {
            var bytes = new byte[2];
            bytes[0] = 0xE3;
            bytes[1] = 0x82;
            return Encoding.UTF8.GetString(bytes);
        }

        public static void ShowLastImage(TreeNode node, ImageList images) =>
            node.ImageIndex = node.SelectedImageIndex = images.Images.Count - 1;

        public static void ShowImage(TreeNode node, int index) => node.ImageIndex = index;

        public static bool IsVistaOrNewer() => Environment.OSVersion.Version.Major >= 6;

        public static bool IsWindows10OrNewer() => Environment.OSVersion.Version.Major >= 10;
    }
}
