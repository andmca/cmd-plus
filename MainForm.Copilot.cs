// Copilot tabs. They start in your user folder, like cmd tabs, unless you pick another folder for that tab.

using System.IO;
using System.Windows.Forms;

partial class MainForm
{
    readonly ToolStripSplitButton copilotButton = new ToolStripSplitButton();

    void NewCopilotTab()
    {
        NewCopilotTab(UserProfile);
    }

    void NewCopilotTab(string folder)
    {
        string name = Path.GetFileName(folder.TrimEnd('\\'));
        if (name.Length == 0) name = folder;   // a drive root, such as C:\
        // /k keeps the cmd prompt open when Copilot exits, so the tab stays usable.
        NewTab("copilot: " + name, "/k copilot", folder, true);
    }

    void ChooseFolderAndStartCopilot()
    {
        using (var dlg = new FolderBrowserDialog { Description = "Folder to start Copilot in", SelectedPath = UserProfile })
        {
            if (dlg.ShowDialog(this) == DialogResult.OK) NewCopilotTab(dlg.SelectedPath);
        }
    }
}
