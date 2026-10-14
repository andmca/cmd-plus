// The folder new Copilot tabs start in, remembered between runs.

using System;
using System.IO;

partial class MainForm
{
    string LoadCopilotFolder()
    {
        try
        {
            string file = File.Exists(settingsFile) ? settingsFile : legacySettingsFile;
            if (File.Exists(file))
            {
                string f = File.ReadAllText(file).Trim();
                if (Directory.Exists(f)) return f;
            }
        }
        catch { }
        return UserProfile;
    }

    void SaveCopilotFolder()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsFile));
            File.WriteAllText(settingsFile, copilotFolder);
        }
        catch { }
    }

    void UpdateCopilotTooltip()
    {
        copilotButton.ToolTipText = "Start Copilot in " + copilotFolder;
    }
}
