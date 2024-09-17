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
using Microsoft.Win32;

namespace WIMExplorer
{
    public partial class Form1 : Form
    {
        internal sealed class NativeMethods
        {
            private NativeMethods()
            {
            }

            [DllImport("dwmapi.dll")]
            public static extern void DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

            public static string GetFileTypeDescription(string fileExtension)
            {
                SHFILEINFO shfi;
                if (IntPtr.Zero != SHGetFileInfo(fileExtension, FILE_ATTRIBUTE_NORMAL, out shfi, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), SHGFI_USEFILEATTRIBUTES | SHGFI_TYPENAME))
                {
                    return shfi.szTypeName;
                }
                return null;
            }

            public static IntPtr GetFileTypeIcon(string fileExtension)
            {
                SHFILEINFO shfi;
                if (IntPtr.Zero != SHGetFileInfo(fileExtension, FILE_ATTRIBUTE_NORMAL, out shfi, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), SHGFI_USEFILEATTRIBUTES | SHGFI_ICON))
                {
                    return shfi.hIcon;
                }
                return IntPtr.Zero;
            }

            [DllImport("shell32.dll")]
            private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, out SHFILEINFO psfi, uint cbFileInfo, uint flags);

            [StructLayout(LayoutKind.Sequential)]
            private struct SHFILEINFO
            {
                public IntPtr hIcon;
                public int iIcon;
                public uint dwAttributes;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
                public string szDisplayName;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
                public string szTypeName;
            }

        }

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int WS_EX_COMPOSITED = 0x20000000;
        const int GWL_EXSTYLE = -20;

        // https://stackoverflow.com/a/3780110

        private const uint FILE_ATTRIBUTE_READONLY = 0x00000001;
        private const uint FILE_ATTRIBUTE_HIDDEN = 0x00000002;
        private const uint FILE_ATTRIBUTE_SYSTEM = 0x00000004;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
        private const uint FILE_ATTRIBUTE_ARCHIVE = 0x00000020;
        private const uint FILE_ATTRIBUTE_DEVICE = 0x00000040;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
        private const uint FILE_ATTRIBUTE_TEMPORARY = 0x00000100;
        private const uint FILE_ATTRIBUTE_SPARSE_FILE = 0x00000200;
        private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;
        private const uint FILE_ATTRIBUTE_COMPRESSED = 0x00000800;
        private const uint FILE_ATTRIBUTE_OFFLINE = 0x00001000;
        private const uint FILE_ATTRIBUTE_NOT_CONTENT_INDEXED = 0x00002000;
        private const uint FILE_ATTRIBUTE_ENCRYPTED = 0x00004000;
        private const uint FILE_ATTRIBUTE_VIRTUAL = 0x00010000;

        private const uint SHGFI_ICON = 0x000000100;     // get icon
        private const uint SHGFI_DISPLAYNAME = 0x000000200;     // get display name
        private const uint SHGFI_TYPENAME = 0x000000400;     // get type name
        private const uint SHGFI_ATTRIBUTES = 0x000000800;     // get attributes
        private const uint SHGFI_ICONLOCATION = 0x000001000;     // get icon location
        private const uint SHGFI_EXETYPE = 0x000002000;     // return exe type
        private const uint SHGFI_SYSICONINDEX = 0x000004000;     // get system icon index
        private const uint SHGFI_LINKOVERLAY = 0x000008000;     // put a link overlay on icon
        private const uint SHGFI_SELECTED = 0x000010000;     // show icon in selected state
        private const uint SHGFI_ATTR_SPECIFIED = 0x000020000;     // get only specified attributes
        private const uint SHGFI_LARGEICON = 0x000000000;     // get large icon
        private const uint SHGFI_SMALLICON = 0x000000001;     // get small icon
        private const uint SHGFI_OPENICON = 0x000000002;     // get open icon
        private const uint SHGFI_SHELLICONSIZE = 0x000000004;     // get shell size icon
        private const uint SHGFI_PIDL = 0x000000008;     // pszPath is a pidl
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;     // use passed dwFileAttribute

        public static void EnableDarkTitleBar(IntPtr hwnd, bool isDarkMode)
        {
            int attribute = isDarkMode ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref attribute, 4);
        }

        public IntPtr GetWindowHandle(Control ctrl)
        {
            return ctrl.Handle;
        }

        public bool IsWindowsVersionOrGreater(int majorVersion, int minorVersion, int buildNumber)
        {
            var version = Environment.OSVersion.Version;
            return version.Major > majorVersion || (version.Major == majorVersion && version.Minor > minorVersion) || (version.Major == majorVersion && version.Minor == minorVersion && version.Build >= buildNumber);
        }

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

        private Stack<string> backHistory = new Stack<string>();
        private Stack<string> nextHistory = new Stack<string>();

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
            if (!File.Exists(Path.Combine(Application.StartupPath, libPath)))
                throw new PlatformNotSupportedException($"Unable to find native library [{libPath}]");
            Wim.GlobalInit(Path.Combine(Application.StartupPath, libPath), InitFlags.None);
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

                try
                {
                    string[] pathParts = selectedPath.Split(new string[] { "\\" }, StringSplitOptions.None);
                    if (selectedPath != "\\")
                    {
                        label4.Text = pathParts[pathParts.Length - 2];
                    }
                    else
                    {
                        label4.Text = "Image root";
                    }
                    label5.Text = $"{listView1.Items.Count} items";
                }
                catch (Exception)
                {
                    label4.Text = selectedPath;
                }

                pictureBox1.Image = imageList1.Images[1];
                informationPanel.Visible = false;

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

                backHistory.Push(currentPath);
                nextHistory.Clear();

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
            UpdateNavigationButtons();
        }

        private async void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (skipAdditionalScans) { return; }

            await Task.Run(() => Invoke(new Action(() => toolStripStatusLabel1.Visible = false)));

            dirsGathered = false;
            imageIndex = comboBox1.SelectedIndex + 1;
            listView1.Items.Clear();
            treeView1.Nodes.Clear();

            backHistory.Clear();
            nextHistory.Clear();

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
            UpdateNavigationButtons();
        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            informationPanel.Visible = (listView1.SelectedItems.Count == 1);

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

                label4.Text = listView1.FocusedItem.Text;

                toolStripStatusLabel2.Visible = true;
                label7.Text = $"{created.ToLongDateString()} - {created.ToShortTimeString()}";
                label9.Text = $"{modified.ToLongDateString()} - {created.ToShortTimeString()}";
                label11.Text = $"{accessed.ToLongDateString()} - {created.ToShortTimeString()}";

                if ((selectedEntry.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
                {
                    pictureBox1.Image = imageList1.Images[1];
                    label5.Text = "File folder";
                    toolStripStatusLabel2.Text = $"Created at {created}, last modified at {modified}, last accessed at {accessed}";
                }
                else
                {
                    fileExt = Path.GetExtension(selectedEntry.FullPath).ToUpper().Replace(".", "").Trim();
                    string extDesc = "";
                    IntPtr fileIcon = IntPtr.Zero;
                    if (fileExt != "")
                    {
                        extDesc = NativeMethods.GetFileTypeDescription($".{fileExt}");
                        fileIcon = NativeMethods.GetFileTypeIcon($".{fileExt}");
                    }
                    else
                    {
                        pictureBox1.Image = imageList1.Images[0];
                    }
                    if (fileIcon != IntPtr.Zero)
                    {
                        pictureBox1.Image = (Icon.FromHandle(fileIcon)).ToBitmap();
                    }
                    toolStripStatusLabel2.Text = $"{(extDesc != "" ? extDesc + ". " : (fileExt != "" ? fileExt + " file. " : ""))}Created at {created}, last modified at {modified}, last accessed at {accessed}";
                    label5.Text = (extDesc != "" ? extDesc : (fileExt != "" ? fileExt + " file" : ""));
                }
            }
            else if (listView1.SelectedItems.Count > 1)
            {
                toolStripStatusLabel2.Visible = true;
                toolStripStatusLabel2.Text = $"{listView1.SelectedItems.Count} items selected";

                pictureBox1.Image = null;
                

                // Configure details pane
                label4.Text = "Multiple selection";
                label5.Text = $"{listView1.SelectedItems.Count} selected items";
            }
            else
            {
                toolStripStatusLabel2.Visible = false;
                pictureBox1.Image = imageList1.Images[1];

                try
                {
                    string[] pathParts = currentPath.Split(new string[] { "\\" }, StringSplitOptions.None);
                    if (currentPath != "\\")
                    {
                        label4.Text = pathParts[pathParts.Length - 2];
                    }
                    else
                    {
                        label4.Text = "Image root";
                    }
                    label5.Text = $"{listView1.Items.Count} items";
                }
                catch (Exception)
                {
                    label4.Text = currentPath;
                }
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
            if (e.Node == currentSelectedNode)
            {
                e.Graphics.FillRectangle(SystemBrushes.Highlight, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Node.Text, treeView1.Font, e.Bounds, SystemColors.HighlightText, TextFormatFlags.VerticalCenter);
            }
            else
            {
                TextRenderer.DrawText(e.Graphics, e.Node.Text, treeView1.Font, e.Bounds, ForeColor, TextFormatFlags.VerticalCenter);
            }
        }

        private async void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (treeView1.SelectedNode != null)
            {
                if (skipAdditionalRefreshes) { return; }

                backHistory.Push(currentPath);
                nextHistory.Clear();

                await Task.Run(() => Invoke(new Action(() => listView1.Items.Clear())));
                await Task.Run(() => Invoke(new Action(() => toolStripStatusLabel2.Visible = false)));

                currentPath = treeView1.SelectedNode.FullPath.Replace(treeView1.Nodes["root"].Text, "").Trim();
                if (!currentPath.EndsWith("\\"))
                    currentPath += "\\";

                await ShowFiles(currentPath);
                await Task.Run(() => Invoke(new Action(() => textBoxPath.Text = currentPath)));

                toolStripStatusLabel1.Text = listView1.Items.Count + " item(s)";
                SetStatus("Ready");
                await Task.Run(() => Invoke(new Action(() => treeView1.SelectedNode = e.Node)));              
                await Task.Run(() => Invoke(new Action(() => treeView1.Refresh())));
                currentSelectedNode = e.Node;
            }
            UpdateNavigationButtons();
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

            // Configure appearance based on system preference
            try
            {
                RegistryKey colorRk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
                int colorValue = (int)colorRk.GetValue("AppsUseLightTheme");
                colorRk.Close();
                EnableDarkTitleBar(Handle, (colorValue == 0));
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
                treeView1.BackColor = bgColor;
                treeView1.ForeColor = fgColor;
                listView1.BackColor = bgColor;
                listView1.ForeColor = fgColor;
                toolStrip1.BackColor = bgColor;
                toolStrip1.ForeColor = fgColor;
                textBox1.BackColor = bgColor;
                textBox1.ForeColor = fgColor;
                comboBox1.BackColor = bgColor;
                comboBox1.ForeColor = fgColor;
                textBoxPath.BackColor = bgColor;
                textBoxPath.ForeColor = fgColor;
                // Set pictures of toolbar buttons
                if (bgColor == Color.FromArgb(48,48,48))
                {
                    toolStripDropDownButton1.Image = Properties.Resources.back_btn_dark;
                    toolStripDropDownButton2.Image = Properties.Resources.next_btn_dark;
                    toolStripButton1.Image = Properties.Resources.up_btn_dark;
                    toolStripButton2.Image = Properties.Resources.go_btn_dark;
                }
                else
                {
                    toolStripDropDownButton1.Image = Properties.Resources.back_btn;
                    toolStripDropDownButton2.Image = Properties.Resources.next_btn;
                    toolStripButton1.Image = Properties.Resources.up_btn;
                    toolStripButton2.Image = Properties.Resources.go_btn;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                // Set light theme
                EnableDarkTitleBar(Handle, false);
            }
        }

        private async void toolStripButton1_Click(object sender, EventArgs e)
        {
            await Task.Run(() => Invoke(new Action(() => toolStripStatusLabel2.Visible = false)));

            backHistory.Push(currentPath);
            nextHistory.Clear();

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

            UpdateNavigationButtons();
        }

        private void textBoxPath_TextChanged(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(textBoxPath.Text)) { return; }
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

                    backHistory.Clear();
                    nextHistory.Clear();

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

                    checkBox1.Enabled = true;
                }
            }
            UpdateNavigationButtons();
        }


        private void UpdateNavigationButtons()
        {
            toolStripDropDownButton1.Enabled = backHistory.Count > 0;
            toolStripDropDownButton2.Enabled = nextHistory.Count > 0;
        }

        private async void toolStripDropDownButton1_Click(object sender, EventArgs e)
        {
            if (backHistory.Count > 0)
            {
                // Move current path to next history
                nextHistory.Push(currentPath);
                // Get the previous path
                currentPath = backHistory.Pop();

                skipAdditionalRefreshes = true;

                await Task.Run(() => Invoke(new Action(() => listView1.Items.Clear())));
                await ShowFiles(currentPath);
                await Task.Run(() => Invoke(new Action(() => textBoxPath.Text = currentPath)));
                await Task.Run(() => Invoke(new Action(() => toolStripStatusLabel1.Text = listView1.Items.Count + " item(s)")));
                SetStatus("Ready");
                SelectNodeByPath("Image Root" + currentPath, false);
                await Task.Run(() => Invoke(new Action(() => treeView1.Focus())));
                await Task.Run(() => Invoke(new Action(() => treeView1.Refresh())));

                skipAdditionalRefreshes = false;
            }
            UpdateNavigationButtons();
        }

        private async void toolStripDropDownButton2_Click(object sender, EventArgs e)
        {
            if (nextHistory.Count > 0)
            {
                // Move current path to back history
                backHistory.Push(currentPath);
                // Get the next path
                currentPath = nextHistory.Pop();

                skipAdditionalRefreshes = true;

                await Task.Run(() => Invoke(new Action(() => listView1.Items.Clear())));
                await ShowFiles(currentPath);
                await Task.Run(() => Invoke(new Action(() => textBoxPath.Text = currentPath)));
                await Task.Run(() => Invoke(new Action(() => toolStripStatusLabel1.Text = listView1.Items.Count + " item(s)")));
                SetStatus("Ready");
                SelectNodeByPath("Image Root" + currentPath, false);
                await Task.Run(() => Invoke(new Action(() => treeView1.Focus())));
                await Task.Run(() => Invoke(new Action(() => treeView1.Refresh())));

                skipAdditionalRefreshes = false;
            }
            UpdateNavigationButtons();
        }

        private async void toolStripButton2_Click(object sender, EventArgs e)
        {
            if ((textBoxPath.Text != "") && (textBoxPath.Text != currentPath))
            {
                skipAdditionalRefreshes = true;

                backHistory.Push(currentPath);
                nextHistory.Clear();

                await Task.Run(() => Invoke(new Action(() => listView1.Items.Clear())));
                await Task.Run(() => Invoke(new Action(() => toolStripStatusLabel2.Visible = false)));

                currentPath = textBoxPath.Text;
                if (!currentPath.EndsWith("\\"))
                    currentPath += "\\";

                await ShowFiles(currentPath);

                await Task.Run(() => Invoke(new Action(() => toolStripStatusLabel1.Text = listView1.Items.Count + " item(s)")));
                SetStatus("Ready");
                SelectNodeByPath("Image Root" + currentPath, false);

                await Task.Run(() => Invoke(new Action(() => treeView1.Focus())));
                await Task.Run(() => Invoke(new Action(() => treeView1.Refresh())));

                skipAdditionalRefreshes = false;

                await Task.Run(() => Invoke(new Action(() => toolStripButton1.Enabled = (currentPath != "\\"))));
            }
            else if (string.IsNullOrWhiteSpace(textBoxPath.Text))
            {
                textBoxPath.Text = currentPath;
            }
            UpdateNavigationButtons();
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            detailsPane.Visible = checkBox1.Checked;
        }
    }
}
