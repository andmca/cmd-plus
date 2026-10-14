// Copilot tabs, and the folder they start in, which is remembered between runs.

using System;
using System.IO;
using System.Windows.Forms;

partial class MainForm
{
    static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    static readonly string CopilotFolderFile = Path.Combine(AppData, "CMDPlus", "copilot-folder.txt");
    // Where the setting lived before the app was renamed from Tabs to CMD+; read if the new file isn't there yet.
    static readonly string LegacyCopilotFolderFile = Path.Combine(AppData, "Tabs", "copilot-folder.txt");

    readonly ToolStripSplitButton copilotButton = new ToolStripSplitButton();
    string copilotFolder = LoadCopilotFolder();

    void NewCopilotTab()
    {
        string folder = Directory.Exists(copilotFolder) ? copilotFolder : UserProfile;
        string name = Path.GetFileName(folder.TrimEnd('\\'));
        if (name.Length == 0) name = folder;   // a drive root, such as C:\
        // /k keeps the cmd prompt open when Copilot exits, so the tab stays usable.
        NewTab("copilot: " + name, "/k copilot", folder, true);
    }

    void ChooseFolderAndStartCopilot()
    {
        using (var dlg = new FolderBrowserDialog { Description = "Folder to start Copilot in", SelectedPath = copilotFolder })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            copilotFolder = dlg.SelectedPath;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CopilotFolderFile));
            File.WriteAllText(CopilotFolderFile, copilotFolder);
        }
        catch { }   // it just won't be remembered next time
        UpdateCopilotTooltip();
        NewCopilotTab();
    }

    static string LoadCopilotFolder()
    {
        try
        {
            string file = File.Exists(CopilotFolderFile) ? CopilotFolderFile : LegacyCopilotFolderFile;
            if (File.Exists(file))
            {
                string folder = File.ReadAllText(file).Trim();
                if (Directory.Exists(folder)) return folder;
            }
        }
        catch { }
        return UserProfile;
    }

    void UpdateCopilotTooltip()
    {
        copilotButton.ToolTipText = "Start Copilot in " + copilotFolder;
    }
}
