using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace WIMExplorer
{
    partial class AboutForm : Form
    {
        public AboutForm()
        {
            InitializeComponent();
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://github.com/CodingWonders");
        }

        private void AboutForm_Load(object sender, EventArgs e)
        {
            labelVersion.Text = $"Version {Application.ProductVersion.ToString()}";
            if (File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DT")))
                textBox1.Text += "This copy is shipped with DISMTools.";

            // Configure appearance based on system preference
            try
            {
                RegistryKey colorRk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
                int colorValue = (int)colorRk.GetValue("AppsUseLightTheme");
                colorRk.Close();
                Form1.EnableDarkTitleBar(Handle, (colorValue == 0));
                Color bgColor = new Color();
                Color fgColor = new Color();
                switch (colorValue)
                {
                    case 0:
                        bgColor = Color.FromArgb(48, 48, 48);
                        fgColor = Color.White;
                        break;
                    case 1:
                        bgColor = Color.FromArgb(239, 239, 242);
                        fgColor = Color.Black;
                        break;
                }
                // Set colors of controls
                BackColor = bgColor;
                ForeColor = fgColor;
                textBox1.BackColor = bgColor;
                textBox1.ForeColor = fgColor;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                // Set light theme
                Form1.EnableDarkTitleBar(Handle, false);
            }
        }

        private void okButton_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
