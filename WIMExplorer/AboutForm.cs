using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;

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
        }

        private void okButton_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
