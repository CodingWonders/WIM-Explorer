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
        bool skipAdditionalRefreshes = false;

        private TreeNode lastNode;
        private TreeNode currentSelectedNode;

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

        private void openFileDialog1_FileOk(object sender, CancelEventArgs e)
        {
            if (File.Exists(openFileDialog1.FileName))
            {
                textBox1.Text = openFileDialog1.FileName;
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
                if (listView1.FocusedItem.ImageIndex != 1) { return; }

                await Task.Run(() => Invoke(new Action(() => toolStripStatusLabel2.Visible = false)));

                skipAdditionalRefreshes = true;

                currentPath += listView1.FocusedItem.Text + "\\";

                await Task.Run(() => Invoke(new Action(() => listView1.Items.Clear())));
                await ShowFiles(currentPath);
                await Task.Run(() => Invoke(new Action(() => textBoxPath.Text = currentPath)));

                await Task.Run(() => Invoke(new Action(() => toolStripStatusLabel1.Text = listView1.Items.Count + " item(s)")));
                SetStatus("Ready");
                SelectNodeByPath("Image Root" + currentPath, false);

                await Task.Run(() => Invoke(new Action(() => treeView1.Focus())));
                await Task.Run(() => Invoke(new Action(() => treeView1.Refresh())));

                skipAdditionalRefreshes = false;

                await Task.Run(() => Invoke(new Action(() => toolStripButton1.Enabled = (currentPath != "\\"))));
            }
        }

        private async void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (skipAdditionalScans) { return; }

            await Task.Run(() => Invoke(new Action(() => toolStripStatusLabel1.Visible = false)));

            dirsGathered = false;
            imageIndex = comboBox1.SelectedIndex + 1;
            listView1.Items.Clear();
            treeView1.Nodes.Clear();

            SetStatus("Getting files and directories of the image. Please wait...");

            // Add root node
            treeView1.Nodes.Add("root", "Image Root");

            await GatherFiles(imageFile, imageIndex);
            currentPath = "\\";
            textBoxPath.Text = currentPath;
            await ShowFiles(currentPath);
            dirsGathered = true;

            // Expand root node
            treeView1.Nodes["root"].Expand();

            await Task.Run(() => Invoke(new Action(() => toolStripStatusLabel1.Visible = true)));
            toolStripStatusLabel1.Text = listView1.Items.Count + " item(s)";

            // Hide selected info
            toolStripStatusLabel2.Visible = false;

            SetStatus("Ready");
        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count == 1)
            {

                int itemIndex;

                itemIndex = listView1.FocusedItem.Index;

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

        private void SelectNodeByPath(string path, bool tryToCollapse)
        {
            if (string.IsNullOrEmpty(path))
                return;

            string[] parts = path.Trim('\\').Split('\\');
            TreeNodeCollection nodes = treeView1.Nodes;
            TreeNode currentNode = null;

            foreach (string part in parts)
            {
                bool nodeFound = false;

                foreach (TreeNode node in nodes)
                {
                    if (node.Text.Equals(part, StringComparison.OrdinalIgnoreCase))
                    {
                        node.Expand();
                        currentNode = node;
                        nodes = node.Nodes;
                        nodeFound = true;
                        break;
                    }
                }

                if (!nodeFound)
                {
                    // Node not found for this part of the path
                    currentNode = null;
                    break;
                }
            }

            // Collapse the last extended node
            if ((tryToCollapse) && (lastNode != null && currentNode != null && IsSubNode(currentNode, lastNode)))
            {
                lastNode.Collapse();
            }

            if (currentNode != null)
            {
                treeView1.SelectedNode = currentNode;
                currentNode.EnsureVisible();
                treeView1.Refresh();
                lastNode = currentNode;
                currentSelectedNode = currentNode;
            }
            else
            {
                MessageBox.Show($"Path '{path}' not found in the tree.");
            }
        }

        private bool IsSubNode(TreeNode parent, TreeNode subNode)
        {
            if (subNode == null || parent == null)
                return false;

            TreeNode currentNode = subNode;
            while (currentNode != null)
            {
                if (currentNode == parent)
                    return true;
                currentNode = currentNode.Parent;
            }
            return false;
        }

        private void treeView1_Leave(object sender, EventArgs e)
        {
            if (currentSelectedNode != null)
            {
                treeView1.SelectedNode = currentSelectedNode;
                currentSelectedNode.EnsureVisible();
            }
        }

        private void treeView1_DrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            if (e.Node == currentSelectedNode && !treeView1.Focused)
            {
                e.Graphics.FillRectangle(SystemBrushes.Highlight, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Node.Text, treeView1.Font, e.Bounds, SystemColors.HighlightText, TextFormatFlags.GlyphOverhangPadding);
            }
            else
            {
                e.DrawDefault = true;
            }
        }

        private async void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (treeView1.SelectedNode != null)
            {
                if (skipAdditionalRefreshes) { return; }

                await Task.Run(() => Invoke(new Action(() => listView1.Items.Clear())));
                await Task.Run(() => Invoke(new Action(() => toolStripStatusLabel2.Visible = false)));

                currentPath = treeView1.SelectedNode.FullPath.Replace(treeView1.Nodes["root"].Text, "").Trim();
                if (!currentPath.EndsWith("\\"))
                    currentPath += "\\";

                await ShowFiles(currentPath);
                await Task.Run(() => Invoke(new Action(() => textBoxPath.Text = currentPath)));

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
                await Task.Run(() => Invoke(new Action(() => treeView1.SelectedNode = e.Node)));              
                await Task.Run(() => Invoke(new Action(() => treeView1.Refresh())));
                currentSelectedNode = e.Node;
            }
        }

        private void Form1_SizeChanged(object sender, EventArgs e)
        {
            if (Visible)
            {
                int left = 0;
                left = (toolStripDropDownButton1.Width + toolStripDropDownButton2.Width + toolStripButton1.Width + toolStripSeparator1.Width + toolStripLabel1.Width);
                textBoxPath.Width = toolStrip1.Width - (left + toolStripButton2.Width) - 10;
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            int left = 0;
            left = (toolStripDropDownButton1.Width + toolStripDropDownButton2.Width + toolStripButton1.Width + toolStripSeparator1.Width + toolStripLabel1.Width);
            textBoxPath.Width = toolStrip1.Width - (left + toolStripButton2.Width) - 10;

            // Gather command-line arguments
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length < 1) { return; }
            foreach (string arg in args)
            {
                if (arg.StartsWith("/image=", StringComparison.OrdinalIgnoreCase))
                {
                    string imagePath;
                    imagePath = arg.Replace("/image=", "").Trim();

                    if (File.Exists(imagePath))
                    {
                        textBox1.Text = imagePath;
                    }
                }
            }
        }

        private async void toolStripButton1_Click(object sender, EventArgs e)
        {
            await Task.Run(() => Invoke(new Action(() => toolStripStatusLabel2.Visible = false)));
            skipAdditionalRefreshes = true;
            string fullPath = currentPath.TrimEnd("\\".ToCharArray());
            List<string> parts = fullPath.Split(new string[] { "\\" }, StringSplitOptions.None).ToList();
            parts[parts.Count - 1] = "";
            currentPath = string.Join("\\", parts);
            await Task.Run(() => Invoke(new Action(() => listView1.Items.Clear())));
            await ShowFiles(currentPath);
            await Task.Run(() => Invoke(new Action(() => textBoxPath.Text = currentPath)));
            
            await Task.Run(() => Invoke(new Action(() => toolStripStatusLabel1.Text = listView1.Items.Count + " item(s)")));
            SetStatus("Ready");
            SelectNodeByPath("Image Root" + currentPath, true);

            await Task.Run(() => Invoke(new Action(() => treeView1.Focus())));
            await Task.Run(() => Invoke(new Action(() => treeView1.Refresh())));

            skipAdditionalRefreshes = false;

            await Task.Run(() => Invoke(new Action(() => toolStripButton1.Enabled = (currentPath != "\\"))));
        }

        private void textBoxPath_TextChanged(object sender, EventArgs e)
        {
            if (currentPath != "\\")
            {
                string[] parts = textBoxPath.Text.Split(new string[] { "\\" }, StringSplitOptions.None);

                toolStripButton2.ToolTipText = $"Go to {parts[parts.Length - 2]}";
            }
            else
            {
                toolStripButton2.ToolTipText = "Go";
            }
        }

        private async void textBox1_TextChanged(object sender, EventArgs e)
        {
            if (File.Exists(textBox1.Text))
            {
                if (imageFile != textBox1.Text)
                {
                    imageIndex = 1;
                    imageFile = textBox1.Text;
                    dirsGathered = false;
                    skipAdditionalScans = true;
                    toolStripStatusLabel1.Visible = false;
                    listView1.Items.Clear();
                    treeView1.Nodes.Clear();
                    comboBox1.Items.Clear();

                    SetStatus("Getting files and directories of the image. Please wait...");

                    // Add root node
                    treeView1.Nodes.Add("root", "Image Root");

                    GetWimIndexes(textBox1.Text);
                    if (comboBox1.Items.Count > 0)
                    {
                        comboBox1.SelectedIndex = 0;
                    }
                    await GatherFiles(imageFile, imageIndex);
                    currentPath = "\\";
                    await ShowFiles(currentPath);
                    textBoxPath.Text = currentPath;
                    dirsGathered = true;
                    skipAdditionalScans = false;

                    // Expand root node
                    treeView1.Nodes["root"].Expand();
                    
                    toolStripStatusLabel1.Visible = true;
                    toolStripStatusLabel1.Text = listView1.Items.Count + " item(s)";

                    // Hide selected info
                    toolStripStatusLabel2.Visible = false;

                    SetStatus("Ready");

                    toolStrip1.Enabled = true;
                }
            }
        }
    }
}
