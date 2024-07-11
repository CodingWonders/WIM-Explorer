using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ManagedWimLib;
using System.Runtime.InteropServices;
using System.IO;
using System.Diagnostics;
using Microsoft.Dism;

namespace WIMExplorer
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            InitializeNativeLibrary();
        }

        List<DirEntry> imgContents = new List<DirEntry>();
        private Dictionary<int, TreeNode> depthNodeMap = new Dictionary<int, TreeNode>();
        string imageFile = "";
        int imageIndex = 1;
        string currentPath = "\\";

        List<DirEntry> contentsInDir = new List<DirEntry>();

        bool dirsGathered = false;
        bool skipAdditionalScans = false;

        public void SetStatus(string statusMsg)
        {
            toolStripStatusLabel3.Visible = true;
            toolStripStatusLabel3.Text = statusMsg;
            Application.DoEvents();
        }

        public static void InitializeNativeLibrary()
        {
            string arch = null;
            switch (RuntimeInformation.ProcessArchitecture)
            {
                case Architecture.X86:
                    arch = "x86";
                    break;
                case Architecture.X64:
                    arch = "x64";
                    break;
                case Architecture.Arm64:
                    arch = "arm64";
                    break;
            }
            string libPath = Path.Combine(arch, "libwim-15.dll");
            if (!File.Exists(libPath))
                throw new PlatformNotSupportedException("Unable to find native library [{libPath}]");
            Wim.GlobalInit(libPath, InitFlags.None);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            openFileDialog1.ShowDialog();
        }

        private async void openFileDialog1_FileOk(object sender, CancelEventArgs e)
        {
            if (File.Exists(openFileDialog1.FileName))
            {
                textBox1.Text = openFileDialog1.FileName;
                if (imageFile != openFileDialog1.FileName)
                {
                    imageIndex = 1;
                    imageFile = openFileDialog1.FileName;
                    dirsGathered = false;
                    skipAdditionalScans = true;
                    listView1.Items.Clear();
                    treeView1.Nodes.Clear();
                    comboBox1.Items.Clear();

                    SetStatus("Getting files and directories of the image. Please wait...");

                    // Add root node
                    treeView1.Nodes.Add("root", "Image Root");

                    GetWimIndexes(openFileDialog1.FileName);
                    if (comboBox1.Items.Count > 0)
                    {
                        comboBox1.SelectedIndex = 0;
                    }
                    await GatherFiles(imageFile, imageIndex);
                    currentPath = "\\";
                    await ShowFiles(currentPath);
                    textBox3.Text = currentPath;
                    dirsGathered = true;
                    skipAdditionalScans = false;

                    // Expand root node
                    treeView1.Nodes["root"].Expand();

                    // Display total count without ".."
                    toolStripStatusLabel1.Visible = true;
                    toolStripStatusLabel1.Text = listView1.Items.Count + " item(s)";

                    // Hide selected info
                    toolStripStatusLabel2.Visible = false;

                    SetStatus("Ready");
                }
            }
        }

        private void GetWimIndexes(string wimFile)
        {
            try
            {
                DismApi.Initialize(DismLogLevel.LogErrors);
                DismImageInfoCollection dismImages = DismApi.GetImageInfo(wimFile);
                foreach (DismImageInfo dismImage in dismImages)
                {
                    comboBox1.Items.Add($"{dismImage.ImageIndex} ({dismImage.ImageName})");
                }
                DismApi.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to get indexes. Error code: {ex.Message}");
            }
        }

        private async Task GatherFiles(string wimFile, int wimIndex)
        {
            try
            {
                imgContents.Clear();
                depthNodeMap.Clear();
                Wim wimHandle = await Task.Run(() => Wim.OpenWim(wimFile, OpenFlags.None));
                IterateDirTreeCallback callback = new IterateDirTreeCallback(DirectoryTreeCallback);
                int result = await Task.Run(() => wimHandle.IterateDirTree(wimIndex, "\\", IterateDirTreeFlags.Recursive, callback));
                if (result != 0)
                {
                    MessageBox.Show($"Failed to iterate directory tree. Error code: {result}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to iterate directory tree. Error code: {ex.Message}");
            }
        }

        private int DirectoryTreeCallback(DirEntry dEntry, object userData)
        {
            // System.Diagnostics.Debug.WriteLine($"Entry: {dEntry.FileName}, Size: {dEntry.Depth}, Attributes: {dEntry.Attributes}");
            imgContents.Add(dEntry);
            return 0;
        }

        private async Task ShowFiles(string selectedPath)
        {
            ulong depth = 0;
            contentsInDir.Clear();

            if (imgContents.Count > 0)
            {
                SetStatus("Please wait...");

                if (selectedPath != "\\")
                {
                    string fullPath = "Image" + currentPath;
                    string[] parts = fullPath.Split(new string[] { "\\" }, StringSplitOptions.None);
                    depth = (ulong)parts.Length - 1;

                    // Create item so that we can go back a directory
                    ListViewItem goBack = new ListViewItem();
                    goBack.Text = "..";
                    goBack.ImageIndex = 2;
                    if (parts.Length > 3)
                    {
                        goBack.ToolTipText = $"Go to the parent directory ({parts[parts.Length - 3]})";
                    }
                    else
                    {
                        goBack.ToolTipText = "Go to the image root";
                    }
                    await Task.Run(() => listView1.Invoke(new Action(() => listView1.Items.Add(goBack))));
                }

                foreach (DirEntry dEntry in imgContents)
                {
                    if (selectedPath == "\\")
                    {
                        if (dEntry.FileName == "" || dEntry.Depth == 0)
                            continue;
                        if (((dEntry.Attributes & FileAttributes.Directory) == FileAttributes.Directory) && !dirsGathered)
                            await AddNodeToTreeView(dEntry);
                        if (dEntry.Depth == 1)
                        {
                            contentsInDir.Add(dEntry);
                        }
                    }
                    else
                    {
                        if (dEntry.FullPath.StartsWith(selectedPath))
                        {
                            if (dEntry.FileName == "" || dEntry.Depth == 0)
                                continue;
                            if (dEntry.Depth == depth)
                            {
                                contentsInDir.Add(dEntry);
                            }
                        }
                    }
                }

                if (contentsInDir.Count > 0)
                {
                    List<ListViewItem> items = new List<ListViewItem>();
                    foreach (DirEntry dEntry in contentsInDir)
                    {
                        ListViewItem lvi = new ListViewItem();
                        lvi.Text = dEntry.FileName;
                        if ((dEntry.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
                        {
                            lvi.ImageIndex = 1;
                        }
                        else
                        {
                            lvi.ImageIndex = 0;
                        }
                        items.Add(lvi);
                    }
                    await Task.Run(() => listView1.Invoke(new Action(() => listView1.Items.AddRange(items.ToArray()))));
                }
            }
        }

        private async Task AddNodeToTreeView(DirEntry dEntry)
        {
            TreeNode newNode = new TreeNode(dEntry.FileName);

            if (dEntry.Depth == 1)
            {
                // Root level directory
                await Task.Run(() => treeView1.Invoke(new Action(() => treeView1.Nodes["root"].Nodes.Add(newNode))));
                depthNodeMap[(int)dEntry.Depth] = newNode;
            }
            else
            {
                // Non-root directory
                if (depthNodeMap.TryGetValue((int)dEntry.Depth - 1, out TreeNode parentNode))
                {
                    parentNode.Nodes.Add(newNode);
                    depthNodeMap[(int)dEntry.Depth] = newNode;
                }
            }
        }

        private async void listView1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (listView1.SelectedItems.Count == 1)
            {
                await Task.Run(() => Invoke(new Action(() => toolStripStatusLabel2.Visible = false)));

                if ((listView1.FocusedItem.Text != "..") && (listView1.FocusedItem.ImageIndex != 1)) { return; }
                if (listView1.FocusedItem.Text == "..")
                {
                    string fullPath = currentPath.TrimEnd("\\".ToCharArray());
                    List<string> parts = fullPath.Split(new string[] { "\\" }, StringSplitOptions.None).ToList();
                    parts[parts.Count - 1] = "";
                    currentPath = string.Join("\\", parts);
                }
                else
                {
                    currentPath += listView1.FocusedItem.Text + "\\";
                }
                listView1.Items.Clear();
                await ShowFiles(currentPath);
                await Task.Run(() => Invoke(new Action(() => textBox3.Text = currentPath)));

                // Display total count without ".."
                await Task.Run(() => Invoke(new Action(() =>
                {
                    if (currentPath == "\\")
                    {
                        toolStripStatusLabel1.Text = listView1.Items.Count + " item(s)";
                    }
                    else
                    {
                        toolStripStatusLabel1.Text = (listView1.Items.Count - 1) + " item(s)";
                    }
                })));
                SetStatus("Ready");
            }
        }

        private async void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (skipAdditionalScans) { return; }

            dirsGathered = false;
            imageIndex = comboBox1.SelectedIndex + 1;
            listView1.Items.Clear();
            treeView1.Nodes.Clear();

            SetStatus("Getting files and directories of the image. Please wait...");

            // Add root node
            treeView1.Nodes.Add("root", "Image Root");

            await GatherFiles(imageFile, imageIndex);
            currentPath = "\\";
            textBox3.Text = currentPath;
            await ShowFiles(currentPath);
            dirsGathered = true;

            // Expand root node
            treeView1.Nodes["root"].Expand();

            // Display total count without ".."
            toolStripStatusLabel1.Text = listView1.Items.Count + " item(s)";

            // Hide selected info
            toolStripStatusLabel2.Visible = false;

            SetStatus("Ready");
        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count == 1)
            {
                if (listView1.FocusedItem.Text == "..") { return; }

                int itemIndex;

                if (listView1.Items[0].Text == "..")
                {
                    itemIndex = listView1.FocusedItem.Index - 1;
                }
                else
                {
                    itemIndex = listView1.FocusedItem.Index;
                }

                DirEntry selectedEntry = contentsInDir[itemIndex];

                // Create property variables
                string fileExt;
                DateTime created = selectedEntry.CreationTime;
                DateTime accessed = selectedEntry.LastAccessTime;
                DateTime modified = selectedEntry.LastWriteTime;

                toolStripStatusLabel2.Visible = true;
                if ((selectedEntry.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
                {
                    toolStripStatusLabel2.Text = $"Created at {created}, last modified at {modified}, last accessed at {accessed}";
                }
                else
                {
                    fileExt = Path.GetExtension(selectedEntry.FullPath).ToUpper().Replace(".", "").Trim();
                    toolStripStatusLabel2.Text = $"{(fileExt != "" ? fileExt + " file. " : "")}Created at {created}, last modified at {modified}, last accessed at {accessed}";
                }
            }
            else if (listView1.SelectedItems.Count > 1)
            {
                toolStripStatusLabel2.Visible = true;
                toolStripStatusLabel2.Text = $"{listView1.SelectedItems.Count} items selected";
            }
            else
            {
                toolStripStatusLabel2.Visible = false;
            }
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            AboutForm about = new AboutForm();
            about.ShowDialog(this);
        }
    }
}
