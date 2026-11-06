using System.Diagnostics;
using TaskbarWidgets.Sdk;

namespace TaskbarWidgets.Widgets;

/// <summary>Widgets système intégrés : un clic ouvre le Gestionnaire des tâches.</summary>
public abstract class SystemWidget : TaskbarWidget
{
    public override void OnClick()
    {
        try
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Élévation refusée par l'utilisateur : rien à faire.
        }
    }
}
