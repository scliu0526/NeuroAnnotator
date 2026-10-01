using System;
using System.Windows.Forms;
using Neurocle.Infrastructure.Persistence;

namespace Neurocle.WinForms
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            System.Windows.Forms.Application.Run(new MainForm(new ProjectLifecycle()));
        }
    }
}
