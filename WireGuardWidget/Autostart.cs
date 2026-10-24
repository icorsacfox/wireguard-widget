using System;
using System.Globalization;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using WireGuardWidget.WireGuard;

namespace WireGuardWidget
{
    /// <summary>
    /// Автозапуск через Планировщик заданий: утилите нужны права администратора,
    /// а записи в Run-ключе реестра запускаются без повышения (и UAC их молча блокирует).
    /// </summary>
    internal static class Autostart
    {
        const string TaskName = "WireGuardWidget";

        /// <summary>Возвращает null при успехе или текст ошибки.</summary>
        public static string Apply(bool enable)
        {
            var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            if (!enable)
            {
                if (!Exists(oem))
                    return null;
                var delete = ProcessRunner.Run("schtasks.exe", "/Delete /TN \"" + TaskName + "\" /F", 15000, oem);
                return delete.Success ? null : delete.Message;
            }

            string xmlPath = Path.Combine(Path.GetTempPath(), "WireGuardWidget.task.xml");
            try
            {
                File.WriteAllText(xmlPath, BuildTaskXml(), Encoding.Unicode);
                var create = ProcessRunner.Run("schtasks.exe", "/Create /TN \"" + TaskName + "\" /XML \"" + xmlPath + "\" /F", 15000, oem);
                return create.Success ? null : create.Message;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            finally
            {
                try { File.Delete(xmlPath); } catch { }
            }
        }

        static bool Exists(Encoding oem)
        {
            return ProcessRunner.Run("schtasks.exe", "/Query /TN \"" + TaskName + "\"", 15000, oem).Success;
        }

        static string BuildTaskXml()
        {
            string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
            string exe = SecurityElement.Escape(Application.ExecutablePath);
            string dir = SecurityElement.Escape(Path.GetDirectoryName(Application.ExecutablePath));
            return
@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>WireGuard Widget — переключатель конфигураций WireGuard</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>" + user + @"</UserId>
      <Delay>PT3S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>" + user + @"</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>5</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>" + exe + @"</Command>
      <WorkingDirectory>" + dir + @"</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
        }
    }
}
