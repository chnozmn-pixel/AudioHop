// AudioHop - quick Windows audio device switcher.
// Lives in the system tray: click to list playback/recording devices and
// switch the default one, or jump straight to a device with a hotkey.
//
// Build:
//   csc /target:winexe /codepage:65001 /out:AudioHop.exe ^
//       /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
//       AudioHop.cs Languages.cs
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AudioHop
{
    internal static class App
    {
        public const string Name = "AudioHop";
    }

    #region Core Audio COM interop

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey
    {
        public Guid fmtid;
        public int pid;
        public PropertyKey(Guid g, int p) { fmtid = g; pid = p; }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    // IPropertyStore refuses QueryInterface on these endpoint objects (E_NOINTERFACE),
    // so GetValue is called straight through the vtable (slot 5).
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int PropertyStoreGetValue(IntPtr self, ref PropertyKey key, IntPtr propVariant);

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    internal class PolicyConfigComObject { }

    // Method order is vtable order; the ones we never call are placeholders.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string id, out IntPtr format);
        [PreserveSig] int GetDeviceFormat(string id, bool def, out IntPtr format);
        [PreserveSig] int ResetDeviceFormat(string id);
        [PreserveSig] int SetDeviceFormat(string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(string id, bool def, out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int SetProcessingPeriod(string id, ref long period);
        [PreserveSig] int GetShareMode(string id, out IntPtr mode);
        [PreserveSig] int SetShareMode(string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue(string id, ref PropertyKey key, IntPtr value);
        [PreserveSig] int SetPropertyValue(string id, ref PropertyKey key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
        [PreserveSig] int SetEndpointVisibility(string id, bool visible);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out int count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(int channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(int channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(int channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(int channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    #endregion

    /// <summary>Master volume and mute for one endpoint, held open while the picker shows it.</summary>
    internal class VolumeControl : IDisposable
    {
        private IAudioEndpointVolume control;
        private Guid context = Guid.Empty;

        public static VolumeControl Open(string deviceId)
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                IMMDevice device;
                if (enumerator.GetDevice(deviceId, out device) != 0 || device == null) return null;

                var iid = typeof(IAudioEndpointVolume).GUID;
                object raw;
                if (device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out raw) != 0 || raw == null)
                    return null;

                return new VolumeControl { control = (IAudioEndpointVolume)raw };
            }
            catch { return null; }
        }

        /// <summary>0..1, or -1 when the endpoint will not say.</summary>
        public float Level
        {
            get
            {
                float level;
                try { return control.GetMasterVolumeLevelScalar(out level) == 0 ? level : -1f; }
                catch { return -1f; }
            }
            set
            {
                if (value < 0f) value = 0f;
                if (value > 1f) value = 1f;
                try { control.SetMasterVolumeLevelScalar(value, ref context); }
                catch { }
            }
        }

        public bool Muted
        {
            get
            {
                bool muted;
                try { return control.GetMute(out muted) == 0 && muted; }
                catch { return false; }
            }
            set
            {
                try { control.SetMute(value, ref context); }
                catch { }
            }
        }

        public void Dispose()
        {
            try { if (control != null) Marshal.ReleaseComObject(control); }
            catch { }
            control = null;
        }
    }

    internal class AudioDevice
    {
        public string Id;
        public string Name;
        public bool IsDefault;
        public bool Unplugged;
        public bool Capture;
        public override string ToString() { return Name; }
    }

    internal static class Audio
    {
        private const int RenderFlow = 0;   // playback (speakers/headphones)
        private const int CaptureFlow = 1;  // recording (microphone)
        private const int StateActive = 1;
        private const int StateUnplugged = 8;
        private static readonly PropertyKey FriendlyName =
            new PropertyKey(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

        public static List<AudioDevice> GetDevices(bool capture)
        {
            try { return Enumerate(capture); }
            catch { return new List<AudioDevice>(); }
        }

        private static List<AudioDevice> Enumerate(bool capture)
        {
            int flow = capture ? CaptureFlow : RenderFlow;
            var result = new List<AudioDevice>();
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

            string defaultId = null;
            IMMDevice def;
            if (enumerator.GetDefaultAudioEndpoint(flow, 0, out def) == 0 && def != null)
                def.GetId(out defaultId);

            IMMDeviceCollection collection;
            if (enumerator.EnumAudioEndpoints(flow, StateActive | StateUnplugged, out collection) != 0)
                return result;

            int count;
            collection.GetCount(out count);
            for (int i = 0; i < count; i++)
            {
                IMMDevice device;
                if (collection.Item(i, out device) != 0) continue;

                string id;
                device.GetId(out id);

                string name = ReadFriendlyName(device) ?? id;

                int state;
                device.GetState(out state);

                result.Add(new AudioDevice
                {
                    Id = id,
                    Name = name,
                    Unplugged = (state & StateUnplugged) != 0,
                    Capture = capture,
                    IsDefault = string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)
                });
            }
            // Connected devices first, then by name
            result.Sort(delegate(AudioDevice a, AudioDevice b)
            {
                if (a.Unplugged != b.Unplugged) return a.Unplugged ? 1 : -1;
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
            });
            return result;
        }

        /// <summary>Is this device the current default endpoint?</summary>
        public static bool IsDefault(string deviceId, bool capture)
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                IMMDevice device;
                if (enumerator.GetDefaultAudioEndpoint(capture ? CaptureFlow : RenderFlow, 0, out device) != 0 || device == null)
                    return false;

                string id;
                device.GetId(out id);
                return string.Equals(id, deviceId, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static string ReadFriendlyName(IMMDevice device)
        {
            IntPtr store;
            if (device.OpenPropertyStore(0 /* STGM_READ */, out store) != 0 || store == IntPtr.Zero)
                return null;

            IntPtr propVariant = Marshal.AllocCoTaskMem(64);
            try
            {
                for (int offset = 0; offset < 64; offset += IntPtr.Size)
                    Marshal.WriteIntPtr(propVariant, offset, IntPtr.Zero);

                IntPtr vtable = Marshal.ReadIntPtr(store);
                IntPtr getValuePtr = Marshal.ReadIntPtr(vtable, 5 * IntPtr.Size);
                var getValue = (PropertyStoreGetValue)Marshal.GetDelegateForFunctionPointer(
                    getValuePtr, typeof(PropertyStoreGetValue));

                var key = FriendlyName;
                if (getValue(store, ref key, propVariant) != 0)
                    return null;

                if (Marshal.ReadInt16(propVariant) != 31 /* VT_LPWSTR */)
                    return null;

                IntPtr text = Marshal.ReadIntPtr(propVariant, 8);
                return text == IntPtr.Zero ? null : Marshal.PtrToStringUni(text);
            }
            catch
            {
                return null;
            }
            finally
            {
                Marshal.FreeCoTaskMem(propVariant);
                Marshal.Release(store);
            }
        }

        public static void SetDefault(string deviceId)
        {
            var config = (IPolicyConfig)new PolicyConfigComObject();
            // 0 = eConsole, 1 = eMultimedia, 2 = eCommunications - all three
            for (int role = 0; role < 3; role++)
            {
                int hr = config.SetDefaultEndpoint(deviceId, role);
                if (hr != 0)
                    throw new System.ComponentModel.Win32Exception(hr,
                        L.T("err.setdefault", hr.ToString("X8")));
            }
        }
    }

    /// <summary>One global hotkey: modifier mask + main key.</summary>
    internal class Hotkey
    {
        public const int ModAlt = 0x0001, ModControl = 0x0002, ModShift = 0x0004, ModWin = 0x0008;

        public int Modifiers;
        public Keys Key;

        public bool IsSet { get { return Key != Keys.None && Modifiers != 0; } }

        public override string ToString()
        {
            if (Key == Keys.None && Modifiers == 0) return "";
            var parts = new List<string>();
            if ((Modifiers & ModControl) != 0) parts.Add("Ctrl");
            if ((Modifiers & ModAlt) != 0) parts.Add("Alt");
            if ((Modifiers & ModShift) != 0) parts.Add("Shift");
            if ((Modifiers & ModWin) != 0) parts.Add("Win");
            if (Key != Keys.None) parts.Add(KeyName(Key));
            return string.Join("+", parts.ToArray());
        }

        private static string KeyName(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9) return ((char)('0' + (key - Keys.D0))).ToString();
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return "Num" + (key - Keys.NumPad0);
            switch (key)
            {
                case Keys.Oemplus: return "+";
                case Keys.OemMinus: return "-";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.Prior: return "PageUp";
                case Keys.Next: return "PageDown";
                default: return key.ToString();
            }
        }

        public static Hotkey Parse(string text)
        {
            var result = new Hotkey();
            if (string.IsNullOrEmpty(text)) return result;

            foreach (var raw in text.Split('+'))
            {
                string part = raw.Trim();
                if (part.Length == 0) continue;
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": result.Modifiers |= ModControl; continue;
                    case "alt": result.Modifiers |= ModAlt; continue;
                    case "shift": result.Modifiers |= ModShift; continue;
                    case "win": result.Modifiers |= ModWin; continue;
                }
                if (part.Length == 1 && part[0] >= '0' && part[0] <= '9')
                {
                    result.Key = Keys.D0 + (part[0] - '0');
                    continue;
                }
                try { result.Key = (Keys)Enum.Parse(typeof(Keys), part, true); }
                catch { }
            }
            return result;
        }
    }

    /// <summary>Checks whether a combination is still free, by briefly claiming it.</summary>
    internal static class HotkeyProbe
    {
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        private const int ProbeId = 0xBEEF;

        public static bool IsFree(IntPtr window, Hotkey hotkey)
        {
            if (!hotkey.IsSet) return true;
            if (!RegisterHotKey(window, ProbeId, hotkey.Modifiers, (int)hotkey.Key)) return false;
            UnregisterHotKey(window, ProbeId);
            return true;
        }

        /// <summary>Combinations that are rarely claimed by other software.</summary>
        public static readonly string[] Candidates =
        {
            "Ctrl+Alt+F9", "Ctrl+Alt+F10", "Ctrl+Shift+F9", "Ctrl+Alt+F11",
            "Ctrl+Shift+F10", "Ctrl+Alt+F12", "Ctrl+Shift+F12"
        };

        /// <summary>First candidate no other program holds and none of `skip` already uses.</summary>
        public static Hotkey FirstFree(IntPtr window, List<Hotkey> skip)
        {
            foreach (var text in Candidates)
            {
                var hotkey = Hotkey.Parse(text);
                bool clash = false;
                if (skip != null)
                    foreach (var other in skip)
                        if (other != null && other.IsSet && other.ToString() == text) clash = true;

                if (!clash && IsFree(window, hotkey)) return hotkey;
            }
            return new Hotkey();
        }
    }

    /// <summary>Settings live in %APPDATA%\AudioHop\settings.ini.</summary>
    internal static class Config
    {
        public static Hotkey CycleHotkey = new Hotkey();          // empty by default - the user decides
        public static Hotkey OverlayHotkey = new Hotkey();        // opens the on-screen picker
        public static readonly Dictionary<string, Hotkey> DeviceHotkeys = new Dictionary<string, Hotkey>();
        public static string Language = "auto";
        public static bool FirstRun;

        public static string Folder
        {
            get
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), App.Name);
                System.IO.Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static string Path { get { return System.IO.Path.Combine(Folder, "settings.ini"); } }

        public static void Load()
        {
            try
            {
                if (!System.IO.File.Exists(Path)) { FirstRun = true; return; }
                DeviceHotkeys.Clear();
                foreach (var line in System.IO.File.ReadAllLines(Path))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().TrimStart('﻿');
                    string value = line.Substring(eq + 1).Trim();

                    if (key == "cycle")
                    {
                        CycleHotkey = Hotkey.Parse(value);
                    }
                    else if (key == "overlay")
                    {
                        OverlayHotkey = Hotkey.Parse(value);
                    }
                    else if (key == "language")
                    {
                        Language = value.Length == 0 ? "auto" : value;
                    }
                    else if (key == "device")
                    {
                        // device=<device id>|<hotkey>
                        int bar = value.LastIndexOf('|');
                        if (bar <= 0) continue;
                        var hotkey = Hotkey.Parse(value.Substring(bar + 1));
                        if (hotkey.IsSet) DeviceHotkeys[value.Substring(0, bar)] = hotkey;
                    }
                }
            }
            catch { /* broken file: carry on with defaults */ }
        }

        public static void Save()
        {
            try
            {
                var lines = new List<string>();
                lines.Add("language=" + Language);
                lines.Add("cycle=" + CycleHotkey);
                lines.Add("overlay=" + OverlayHotkey);
                foreach (var pair in DeviceHotkeys)
                    if (pair.Value.IsSet)
                        lines.Add("device=" + pair.Key + "|" + pair.Value);
                System.IO.File.WriteAllLines(Path, lines.ToArray());
                FirstRun = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(L.T("err.save", ex.Message), App.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    /// <summary>HKCU Run entry, so the tray icon comes back after a reboot.</summary>
    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool Enabled
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                        return key != null && key.GetValue(App.Name) != null;
                }
                catch { return false; }
            }
            set
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                    {
                        if (key == null) return;
                        if (value) key.SetValue(App.Name, "\"" + Application.ExecutablePath + "\"");
                        else if (key.GetValue(App.Name) != null) key.DeleteValue(App.Name);
                    }
                }
                catch { }
            }
        }
    }

    /// <summary>A box that captures the key combination you press.</summary>
    internal class HotkeyBox : TextBox
    {
        private Hotkey value = new Hotkey();
        public event EventHandler ValueChanged;

        public HotkeyBox()
        {
            ReadOnly = true;
            ShortcutsEnabled = false;
            BackColor = SystemColors.Window;
            Cursor = Cursors.Hand;
            Text = L.T("status.none");
        }

        public Hotkey Value
        {
            get { return value; }
            set
            {
                this.value = value ?? new Hotkey();
                Text = this.value.IsSet ? this.value.ToString() : L.T("status.none");
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        public void ClearHotkey() { Value = new Hotkey(); }

        protected override bool IsInputKey(Keys keyData) { return true; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;

            if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
            {
                ClearHotkey();
                return;
            }

            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.Menu ||
                e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
            {
                Text = "...";
                return;
            }

            int modifiers = 0;
            if (e.Control) modifiers |= Hotkey.ModControl;
            if (e.Alt) modifiers |= Hotkey.ModAlt;
            if (e.Shift) modifiers |= Hotkey.ModShift;

            Value = new Hotkey { Modifiers = modifiers, Key = e.KeyCode };
        }

        protected override void OnEnter(EventArgs e)
        {
            base.OnEnter(e);
            BackColor = Color.FromArgb(255, 250, 205);
        }

        protected override void OnLeave(EventArgs e)
        {
            base.OnLeave(e);
            BackColor = SystemColors.Window;
            if (!value.IsSet) Value = value;   // wipe a half-typed "..."
        }
    }

    /// <summary>Shared layout bits and validation for the settings window and the wizard.</summary>
    internal static class Ui
    {
        public const int FormWidth = 540, BoxX = 300, BoxWidth = 150, ClearX = 458, RowHeight = 30;

        public static void ApplyDirection(Form form)
        {
            if (!L.Rtl) return;
            form.RightToLeft = RightToLeft.Yes;
            form.RightToLeftLayout = true;
        }

        public static Button ClearButton(HotkeyBox box, int x, int y)
        {
            var button = new Button { Text = L.T("btn.clear"), TabStop = false };
            button.SetBounds(x, y, 72, 23);
            button.Click += delegate { box.ClearHotkey(); };
            return button;
        }

        /// <summary>Lays out one row per device and returns the next free y.</summary>
        public static int AddDeviceRows(Control parent, ToolTip tips, List<AudioDevice> devices,
            int y, List<KeyValuePair<AudioDevice, HotkeyBox>> into)
        {
            foreach (var device in devices)
            {
                var box = new HotkeyBox();
                Hotkey saved;
                if (Config.DeviceHotkeys.TryGetValue(device.Id, out saved)) box.Value = saved;

                string text = device.Name + (device.Unplugged ? "  " + L.T("menu.unplugged") : "");
                var label = new Label
                {
                    Text = text,
                    Location = new Point(26, y + 4),
                    Size = new Size(BoxX - 36, 19),
                    AutoEllipsis = true,
                    UseMnemonic = false,
                    ForeColor = device.Unplugged ? SystemColors.GrayText : SystemColors.ControlText
                };
                if (tips != null) tips.SetToolTip(label, text);
                parent.Controls.Add(label);

                box.SetBounds(BoxX, y, BoxWidth, 23);
                parent.Controls.Add(box);
                parent.Controls.Add(ClearButton(box, ClearX, y));
                into.Add(new KeyValuePair<AudioDevice, HotkeyBox>(device, box));
                y += RowHeight;
            }
            return y;
        }

        /// <summary>Returns an error message, or null when the set of hotkeys is usable.</summary>
        public static string Validate(List<KeyValuePair<string, Hotkey>> entries)
        {
            foreach (var entry in entries)
                if (entry.Value.Key != Keys.None && entry.Value.Modifiers == 0)
                    return L.T("err.modifier", entry.Key);

            for (int i = 0; i < entries.Count; i++)
                for (int j = i + 1; j < entries.Count; j++)
                    if (entries[i].Value.IsSet &&
                        entries[i].Value.ToString() == entries[j].Value.ToString())
                        return L.T("err.duplicate", entries[i].Value, entries[i].Key, entries[j].Key);

            return null;
        }
    }

    internal class SettingsForm : Form
    {
        private readonly HotkeyBox cycleBox = new HotkeyBox();
        private readonly HotkeyBox overlayBox = new HotkeyBox();
        private readonly ComboBox languageBox = new ComboBox();
        private readonly List<KeyValuePair<AudioDevice, HotkeyBox>> deviceBoxes =
            new List<KeyValuePair<AudioDevice, HotkeyBox>>();
        private readonly ToolTip tips = new ToolTip();

        /// <summary>True when the chosen language differs from the one in use.</summary>
        public bool LanguageChanged;

        public SettingsForm(List<AudioDevice> outputs)
        {
            Text = L.T("settings.title", App.Name);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = MinimizeBox = false;
            Font = SystemFonts.MessageBoxFont;
            Ui.ApplyDirection(this);

            int y = 14;

            Controls.Add(new Label
            {
                Text = L.T("settings.overlay"),
                Location = new Point(14, y + 4),
                Size = new Size(Ui.BoxX - 24, 19),
                AutoEllipsis = true
            });
            overlayBox.Value = Config.OverlayHotkey;
            overlayBox.SetBounds(Ui.BoxX, y, Ui.BoxWidth, 23);
            Controls.Add(overlayBox);
            Controls.Add(Ui.ClearButton(overlayBox, Ui.ClearX, y));
            y += 32;

            Controls.Add(new Label
            {
                Text = L.T("settings.cycle"),
                Location = new Point(14, y + 4),
                Size = new Size(Ui.BoxX - 24, 19),
                AutoEllipsis = true
            });
            cycleBox.Value = Config.CycleHotkey;
            cycleBox.SetBounds(Ui.BoxX, y, Ui.BoxWidth, 23);
            Controls.Add(cycleBox);
            Controls.Add(Ui.ClearButton(cycleBox, Ui.ClearX, y));
            y += 38;

            Controls.Add(new Label
            {
                Text = L.T("settings.direct"),
                Location = new Point(14, y),
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold)
            });
            y += 26;

            y = Ui.AddDeviceRows(this, tips, outputs, y, deviceBoxes);
            y += 10;

            Controls.Add(new Label
            {
                Text = L.T("settings.language"),
                Location = new Point(14, y + 4),
                AutoSize = true
            });
            languageBox.DropDownStyle = ComboBoxStyle.DropDownList;
            languageBox.SetBounds(Ui.BoxX, y, Ui.BoxWidth + 50, 23);
            languageBox.Items.Add(new LanguageChoice("auto", L.T("lang.auto")));
            foreach (var pair in L.Available)
                languageBox.Items.Add(new LanguageChoice(pair.Key, pair.Value));
            foreach (LanguageChoice choice in languageBox.Items)
                if (choice.Code == Config.Language) languageBox.SelectedItem = choice;
            if (languageBox.SelectedItem == null) languageBox.SelectedIndex = 0;
            Controls.Add(languageBox);
            y += 36;

            Controls.Add(new Label
            {
                Text = L.T("settings.hint"),
                Location = new Point(14, y),
                Size = new Size(Ui.FormWidth - 28, 34),
                ForeColor = SystemColors.GrayText
            });
            y += 40;

            var ok = new Button { Text = L.T("btn.save"), DialogResult = DialogResult.OK };
            ok.SetBounds(Ui.FormWidth - 14 - 95 - 8 - 95, y, 95, 28);
            var cancel = new Button { Text = L.T("btn.cancel"), DialogResult = DialogResult.Cancel };
            cancel.SetBounds(Ui.FormWidth - 14 - 95, y, 95, 28);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            ClientSize = new Size(Ui.FormWidth, y + 28 + 14);
            FormClosing += OnClosing;
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.OK) return;

            var entries = new List<KeyValuePair<string, Hotkey>>();
            entries.Add(new KeyValuePair<string, Hotkey>(L.T("overlay.name"), overlayBox.Value));
            entries.Add(new KeyValuePair<string, Hotkey>(L.T("cycle.name"), cycleBox.Value));
            foreach (var pair in deviceBoxes)
                entries.Add(new KeyValuePair<string, Hotkey>(pair.Key.Name, pair.Value.Value));

            string error = Ui.Validate(entries);
            if (error != null)
            {
                MessageBox.Show(error, App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                return;
            }

            Config.OverlayHotkey = overlayBox.Value;
            Config.CycleHotkey = cycleBox.Value;
            Config.DeviceHotkeys.Clear();
            foreach (var pair in deviceBoxes)
                if (pair.Value.Value.IsSet)
                    Config.DeviceHotkeys[pair.Key.Id] = pair.Value.Value;

            var picked = (LanguageChoice)languageBox.SelectedItem;
            LanguageChanged = picked.Code != Config.Language;
            Config.Language = picked.Code;

            Config.Save();
            if (LanguageChanged) L.Init(Config.Language);
        }

        internal class LanguageChoice
        {
            public readonly string Code, Label;
            public LanguageChoice(string code, string label) { Code = code; Label = label; }
            public override string ToString() { return Label; }
        }
    }

    /// <summary>Three-step first-run setup: language, cycle hotkey, per-device hotkeys.</summary>
    internal class WizardForm : Form
    {
        private readonly Panel[] pages = new Panel[3];
        private int page;

        private readonly ComboBox languageBox = new ComboBox();
        private readonly HotkeyBox cycleBox = new HotkeyBox();
        private readonly Label cycleStatus = new Label();
        private readonly HotkeyBox overlayBox = new HotkeyBox();
        private readonly Label overlayStatus = new Label();
        private readonly CheckBox autostart = new CheckBox();
        private readonly List<KeyValuePair<AudioDevice, HotkeyBox>> deviceBoxes =
            new List<KeyValuePair<AudioDevice, HotkeyBox>>();
        private readonly ToolTip tips = new ToolTip();
        private readonly List<AudioDevice> outputs;

        private readonly Button back = new Button();
        private readonly Button next = new Button();
        private readonly Button skip = new Button();

        public WizardForm(List<AudioDevice> outputs, List<AudioDevice> inputs)
        {
            this.outputs = outputs;

            Text = L.T("wizard.title", App.Name);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = MinimizeBox = false;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(Ui.FormWidth, 360);
            Ui.ApplyDirection(this);

            BuildWelcome(outputs.Count, inputs.Count);
            BuildHotkey();
            BuildDevices();

            foreach (var panel in pages)
            {
                panel.SetBounds(0, 0, Ui.FormWidth, 300);
                panel.Visible = false;
                Controls.Add(panel);
            }

            skip.Text = L.T("btn.skip");
            skip.SetBounds(14, 316, 95, 28);
            skip.Click += delegate { Finish(); };
            Controls.Add(skip);

            back.Text = L.T("btn.back");
            back.SetBounds(Ui.FormWidth - 14 - 95 - 8 - 95, 316, 95, 28);
            back.Click += delegate { Show(page - 1); };
            Controls.Add(back);

            next.Text = L.T("btn.next");
            next.SetBounds(Ui.FormWidth - 14 - 95, 316, 95, 28);
            next.Click += delegate
            {
                if (page == pages.Length - 1) Finish();
                else Show(page + 1);
            };
            Controls.Add(next);
            AcceptButton = next;

            Show(0);
        }

        private Label Title(string text)
        {
            return new Label
            {
                Text = text,
                Location = new Point(18, 18),
                Size = new Size(Ui.FormWidth - 36, 26),
                Font = new Font(Font.FontFamily, Font.Size + 3f, FontStyle.Bold)
            };
        }

        private Label Body(string text, int y, int height)
        {
            return new Label
            {
                Text = text,
                Location = new Point(18, y),
                Size = new Size(Ui.FormWidth - 36, height)
            };
        }

        private void BuildWelcome(int outputCount, int inputCount)
        {
            var panel = new Panel();
            panel.Controls.Add(Title(L.T("w1.title", App.Name)));
            panel.Controls.Add(Body(L.T("w1.body", App.Name, outputCount, inputCount), 56, 90));

            panel.Controls.Add(new Label
            {
                Text = L.T("settings.language"),
                Location = new Point(18, 160),
                AutoSize = true
            });
            languageBox.DropDownStyle = ComboBoxStyle.DropDownList;
            languageBox.SetBounds(140, 156, 200, 23);
            languageBox.Items.Add(new SettingsForm.LanguageChoice("auto", L.T("lang.auto")));
            foreach (var pair in L.Available)
                languageBox.Items.Add(new SettingsForm.LanguageChoice(pair.Key, pair.Value));
            foreach (SettingsForm.LanguageChoice choice in languageBox.Items)
                if (choice.Code == Config.Language) languageBox.SelectedItem = choice;
            if (languageBox.SelectedItem == null) languageBox.SelectedIndex = 0;
            languageBox.SelectedIndexChanged += delegate
            {
                var picked = (SettingsForm.LanguageChoice)languageBox.SelectedItem;
                if (picked.Code == Config.Language) return;
                Config.Language = picked.Code;
                L.Init(Config.Language);
                Relaunch = true;      // rebuild the wizard in the new language
                DialogResult = DialogResult.Retry;
                Close();
            };
            panel.Controls.Add(languageBox);
            pages[0] = panel;
        }

        private void BuildHotkey()
        {
            var panel = new Panel();
            panel.Controls.Add(Title(L.T("w2.title")));
            panel.Controls.Add(Body(L.T("w2.body"), 54, 44));

            AddHotkeyBlock(panel, L.T("settings.overlay"), overlayBox, overlayStatus, 108);
            AddHotkeyBlock(panel, L.T("settings.cycle"), cycleBox, cycleStatus, 186);

            pages[1] = panel;
        }

        /// <summary>Label, picker, suggest/clear buttons and a live "is it free?" line.</summary>
        private void AddHotkeyBlock(Panel panel, string caption, HotkeyBox box, Label status, int y)
        {
            panel.Controls.Add(new Label
            {
                Text = caption,
                Location = new Point(18, y),
                Size = new Size(Ui.FormWidth - 36, 18),
                Font = new Font(Font, FontStyle.Bold)
            });

            box.SetBounds(18, y + 20, Ui.BoxWidth, 23);
            box.ValueChanged += delegate { UpdateStatus(box, status); };
            panel.Controls.Add(box);
            panel.Controls.Add(Ui.ClearButton(box, 18 + Ui.BoxWidth + 8, y + 20));

            var suggest = new Button { Text = L.T("w2.suggest"), TabStop = false };
            suggest.SetBounds(18 + Ui.BoxWidth + 8 + 72 + 8, y + 20, 194, 23);
            suggest.Click += delegate { box.Value = HotkeyProbe.FirstFree(Handle, Taken()); };
            panel.Controls.Add(suggest);

            status.SetBounds(18, y + 48, Ui.FormWidth - 36, 18);
            panel.Controls.Add(status);
        }

        /// <summary>Combinations already picked here, so a suggestion never collides.</summary>
        private List<Hotkey> Taken()
        {
            var list = new List<Hotkey> { overlayBox.Value, cycleBox.Value };
            foreach (var pair in deviceBoxes) list.Add(pair.Value.Value);
            return list;
        }

        private void BuildDevices()
        {
            var panel = new Panel();
            panel.Controls.Add(Title(L.T("w3.title")));
            panel.Controls.Add(Body(L.T("w3.body"), 56, 36));

            int y = Ui.AddDeviceRows(panel, tips, outputs, 100, deviceBoxes);

            autostart.Text = L.T("w3.autostart", App.Name);
            autostart.Checked = Autostart.Enabled;
            autostart.SetBounds(18, Math.Min(y + 12, 250), Ui.FormWidth - 36, 24);
            panel.Controls.Add(autostart);

            pages[2] = panel;
        }

        /// <summary>Set when the language changed and the window must be rebuilt.</summary>
        public bool Relaunch;

        private void UpdateStatus(HotkeyBox box, Label status)
        {
            if (!box.Value.IsSet)
            {
                status.Text = L.T("w2.empty");
                status.ForeColor = SystemColors.GrayText;
                return;
            }
            bool free = HotkeyProbe.IsFree(Handle, box.Value);
            status.Text = free ? L.T("w2.free") : L.T("w2.taken");
            status.ForeColor = free ? Color.FromArgb(0, 128, 0) : Color.FromArgb(176, 0, 0);
        }

        private void Show(int index)
        {
            page = Math.Max(0, Math.Min(pages.Length - 1, index));
            for (int i = 0; i < pages.Length; i++) pages[i].Visible = i == page;

            back.Enabled = page > 0;
            next.Text = page == pages.Length - 1 ? L.T("btn.finish") : L.T("btn.next");
            skip.Visible = page < pages.Length - 1;

            if (page == 1)
            {
                if (!cycleBox.Value.IsSet && Config.CycleHotkey.IsSet) cycleBox.Value = Config.CycleHotkey;
                if (!overlayBox.Value.IsSet && Config.OverlayHotkey.IsSet) overlayBox.Value = Config.OverlayHotkey;
                UpdateStatus(overlayBox, overlayStatus);
                UpdateStatus(cycleBox, cycleStatus);
            }
        }

        private void Finish()
        {
            var entries = new List<KeyValuePair<string, Hotkey>>();
            entries.Add(new KeyValuePair<string, Hotkey>(L.T("overlay.name"), overlayBox.Value));
            entries.Add(new KeyValuePair<string, Hotkey>(L.T("cycle.name"), cycleBox.Value));
            foreach (var pair in deviceBoxes)
                entries.Add(new KeyValuePair<string, Hotkey>(pair.Key.Name, pair.Value.Value));

            string error = Ui.Validate(entries);
            if (error != null)
            {
                MessageBox.Show(error, App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Config.OverlayHotkey = overlayBox.Value;
            Config.CycleHotkey = cycleBox.Value;
            Config.DeviceHotkeys.Clear();
            foreach (var pair in deviceBoxes)
                if (pair.Value.Value.IsSet)
                    Config.DeviceHotkeys[pair.Key.Id] = pair.Value.Value;

            Autostart.Enabled = autostart.Checked;
            Config.Save();
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>
    /// The on-screen picker: a borderless panel that floats above everything,
    /// opened by a hotkey. Arrow keys or the mouse pick a device, Enter confirms,
    /// Esc closes. Digits 1-9 jump straight to a row.
    /// </summary>
    internal class OverlayForm : Form
    {
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

        private class Row
        {
            public AudioDevice Device;
            public string Text;
            public bool Header;
            public int Number;
        }

        private const int PanelWidth = 470, Pad = 16, RowHeight = 36, HeaderHeight = 28,
                          TitleHeight = 38, HintHeight = 30, Radius = 12, VolumeHeight = 46;

        private static readonly Color BackColour = Color.FromArgb(32, 32, 32);
        private static readonly Color EdgeColour = Color.FromArgb(72, 72, 72);
        private static readonly Color TextColour = Color.FromArgb(240, 240, 240);
        private static readonly Color DimColour = Color.FromArgb(150, 150, 150);
        private static readonly Color AccentColour = Color.FromArgb(0, 110, 200);
        private static readonly Color HoverColour = Color.FromArgb(58, 58, 58);

        private readonly List<Row> rows = new List<Row>();
        private readonly Font titleFont, rowFont, smallFont;
        private readonly Timer fade = new Timer();
        private int selected = -1;
        private int hover = -1;

        private VolumeControl mixer;      // endpoint behind the slider
        private string mixerDeviceId;
        private float volume = -1f;       // -1 when the endpoint has no volume to show
        private bool muted;
        private bool dragging;

        /// <summary>The device the user picked, or null when dismissed.</summary>
        public AudioDevice Chosen;

        public OverlayForm(List<AudioDevice> outputs, List<AudioDevice> inputs)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            BackColor = BackColour;
            StartPosition = FormStartPosition.Manual;

            titleFont = new Font("Segoe UI", 11f, FontStyle.Bold);
            rowFont = new Font("Segoe UI", 10f);
            smallFont = new Font("Segoe UI", 8.25f);

            int number = 1;
            BuildSection(L.T("menu.output"), outputs, ref number);
            BuildSection(L.T("menu.input"), inputs, ref number);

            int height = TitleHeight + VolumeHeight + HintHeight + Pad;
            foreach (var row in rows) height += row.Header ? HeaderHeight : RowHeight;
            Size = new Size(PanelWidth, height);

            var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(screen.Left + (screen.Width - Width) / 2,
                                 screen.Top + (screen.Height - Height) / 2);

            // Start on the device that is in use, so Enter alone is harmless.
            for (int i = 0; i < rows.Count; i++)
                if (!rows[i].Header && rows[i].Device.IsDefault && !rows[i].Device.Capture)
                { selected = i; break; }
            if (selected < 0) selected = FirstSelectable(0, 1);
            AttachMixer();

            Opacity = 0;
            fade.Interval = 15;
            fade.Tick += delegate
            {
                if (Opacity >= 0.96) { Opacity = 0.97; fade.Stop(); }
                else Opacity += 0.16;
            };
        }

        private void BuildSection(string header, List<AudioDevice> devices, ref int number)
        {
            if (devices.Count == 0) return;
            rows.Add(new Row { Header = true, Text = header });
            foreach (var device in devices)
                rows.Add(new Row { Device = device, Text = device.Name, Number = number <= 9 ? number++ : 0 });
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW - keep it out of Alt+Tab
                p.ExStyle |= 0x00000008;   // WS_EX_TOPMOST
                return p;
            }
        }

        private DateTime shownAt = DateTime.UtcNow;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            shownAt = DateTime.UtcNow;
            // The hotkey press gives us the right to come to the front.
            SetForegroundWindow(Handle);
            Activate();
            fade.Start();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            using (var path = RoundedRect(new Rectangle(0, 0, Width, Height), Radius))
                Region = new Region(path);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            // Focus can bounce around in the first moments after a window appears;
            // only treat a later loss of focus as "the user clicked elsewhere".
            if (DateTime.UtcNow - shownAt < TimeSpan.FromMilliseconds(600)) return;
            Dismiss();   // dismiss it like the Game Bar does
        }

        /// <summary>
        /// Closes once. Closing deactivates the window, which would otherwise come
        /// straight back through OnDeactivate and close it a second time.
        /// </summary>
        public void Dismiss()
        {
            if (closing) return;
            closing = true;
            Close();
        }

        private bool closing;

        private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private int RowTop(int index)
        {
            int y = TitleHeight;
            for (int i = 0; i < index; i++) y += rows[i].Header ? HeaderHeight : RowHeight;
            return y;
        }

        private int RowAt(int pixelY)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                int top = RowTop(i);
                int height = rows[i].Header ? HeaderHeight : RowHeight;
                if (pixelY >= top && pixelY < top + height) return rows[i].Header ? -1 : i;
            }
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(BackColour);

            using (var pen = new Pen(EdgeColour))
            using (var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
                g.DrawPath(pen, path);

            using (var brush = new SolidBrush(TextColour))
                g.DrawString(L.T("overlay.title"), titleFont, brush, Pad, 10);

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                int top = RowTop(i);

                if (row.Header)
                {
                    using (var brush = new SolidBrush(DimColour))
                        g.DrawString(row.Text, smallFont, brush, Pad, top + 9);
                    continue;
                }

                var area = new Rectangle(Pad - 6, top + 2, Width - 2 * (Pad - 6), RowHeight - 4);
                if (i == selected)
                    using (var brush = new SolidBrush(AccentColour))
                    using (var path = RoundedRect(area, 6))
                        g.FillPath(brush, path);
                else if (i == hover)
                    using (var brush = new SolidBrush(HoverColour))
                    using (var path = RoundedRect(area, 6))
                        g.FillPath(brush, path);

                // A dot marks the device currently in use.
                if (row.Device.IsDefault)
                    using (var brush = new SolidBrush(i == selected ? Color.White : AccentColour))
                        g.FillEllipse(brush, Pad + 2, top + RowHeight / 2 - 4, 8, 8);

                var colour = row.Device.Unplugged && i != selected ? DimColour : TextColour;
                string text = row.Text + (row.Device.Unplugged ? "   " + L.T("menu.unplugged") : "");

                var textArea = new RectangleF(Pad + 20, top + 8, Width - Pad * 2 - 54, RowHeight - 12);
                using (var brush = new SolidBrush(colour))
                using (var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    g.DrawString(text, rowFont, brush, textArea, format);

                if (row.Number > 0)
                    using (var brush = new SolidBrush(i == selected ? Color.White : DimColour))
                        g.DrawString(row.Number.ToString(), smallFont, brush, Width - Pad - 14, top + 11);
            }

            DrawVolume(g);

            using (var brush = new SolidBrush(DimColour))
                g.DrawString(L.T("overlay.hint"), smallFont, brush, Pad, Height - HintHeight + 4);
        }

        private int VolumeTop { get { return Height - HintHeight - VolumeHeight; } }
        private Rectangle TrackArea
        {
            get { return new Rectangle(Pad + 30, VolumeTop + 20, Width - 2 * Pad - 30 - 54, 6); }
        }
        private Rectangle SpeakerArea
        {
            get { return new Rectangle(Pad, VolumeTop + 12, 24, 22); }
        }

        /// <summary>The slider follows whichever row is highlighted.</summary>
        private void DrawVolume(Graphics g)
        {
            var track = TrackArea;
            bool enabled = volume >= 0f;
            var fill = muted || !enabled ? Color.FromArgb(110, 110, 110) : AccentColour;

            DrawSpeaker(g, SpeakerArea, enabled ? (muted ? DimColour : TextColour) : Color.FromArgb(90, 90, 90));

            using (var brush = new SolidBrush(Color.FromArgb(70, 70, 70)))
            using (var path = RoundedRect(track, 3))
                g.FillPath(brush, path);

            if (enabled && volume > 0f)
            {
                var done = new Rectangle(track.X, track.Y, (int)(track.Width * volume), track.Height);
                if (done.Width >= 6)
                    using (var brush = new SolidBrush(fill))
                    using (var path = RoundedRect(done, 3))
                        g.FillPath(brush, path);
            }

            if (enabled)
            {
                int knobX = track.X + (int)(track.Width * volume);
                using (var brush = new SolidBrush(muted ? Color.FromArgb(150, 150, 150) : Color.White))
                    g.FillEllipse(brush, knobX - 7, track.Y - 5, 15, 15);
            }

            string text = !enabled ? "—" : muted ? L.T("overlay.muted") : (int)Math.Round(volume * 100) + "%";
            using (var brush = new SolidBrush(enabled && !muted ? TextColour : DimColour))
            using (var format = new StringFormat { Alignment = StringAlignment.Far })
                g.DrawString(text, smallFont, brush,
                    new RectangleF(Width - Pad - 50, VolumeTop + 15, 50, 18), format);
        }

        private static void DrawSpeaker(Graphics g, Rectangle r, Color colour)
        {
            using (var brush = new SolidBrush(colour))
            {
                g.FillRectangle(brush, r.X + 1, r.Y + 7, 4, 8);
                g.FillPolygon(brush, new[]
                {
                    new Point(r.X + 5, r.Y + 7), new Point(r.X + 11, r.Y + 2),
                    new Point(r.X + 11, r.Y + 20), new Point(r.X + 5, r.Y + 15)
                });
            }
            using (var pen = new Pen(colour, 1.6f))
            {
                g.DrawArc(pen, r.X + 11, r.Y + 5, 7, 12, -60, 120);
                g.DrawArc(pen, r.X + 13, r.Y + 1, 11, 20, -60, 120);
            }
        }

        /// <summary>Points the slider at the highlighted device.</summary>
        private void AttachMixer()
        {
            string id = selected >= 0 && !rows[selected].Header ? rows[selected].Device.Id : null;
            if (id == mixerDeviceId) return;

            if (mixer != null) { mixer.Dispose(); mixer = null; }
            mixerDeviceId = id;
            volume = -1f;
            muted = false;

            if (id == null) return;
            mixer = VolumeControl.Open(id);
            if (mixer == null) return;

            volume = mixer.Level;
            muted = mixer.Muted;
        }

        private void SetVolume(float level)
        {
            if (mixer == null || volume < 0f) return;
            if (level < 0f) level = 0f;
            if (level > 1f) level = 1f;

            volume = level;
            mixer.Level = level;
            if (muted && level > 0f) { muted = false; mixer.Muted = false; }
            Invalidate();
        }

        private void ToggleMute()
        {
            if (mixer == null || volume < 0f) return;
            muted = !muted;
            mixer.Muted = muted;
            Invalidate();
        }

        private int FirstSelectable(int from, int step)
        {
            for (int i = from; i >= 0 && i < rows.Count; i += step)
                if (!rows[i].Header) return i;
            return -1;
        }

        private void MoveSelection(int step)
        {
            int index = selected;
            for (int guard = 0; guard < rows.Count; guard++)
            {
                index += step;
                if (index < 0) index = rows.Count - 1;
                if (index >= rows.Count) index = 0;
                if (!rows[index].Header) { selected = index; AttachMixer(); Invalidate(); return; }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
            switch (e.KeyCode)
            {
                case Keys.Escape: Dismiss(); return;
                case Keys.Up: MoveSelection(-1); return;
                case Keys.Down: MoveSelection(1); return;
                case Keys.Home: selected = FirstSelectable(0, 1); AttachMixer(); Invalidate(); return;
                case Keys.End: selected = FirstSelectable(rows.Count - 1, -1); AttachMixer(); Invalidate(); return;
                case Keys.Enter:
                case Keys.Space: Choose(selected); return;
                case Keys.Left: SetVolume(volume - (e.Shift ? 0.01f : 0.05f)); return;
                case Keys.Right: SetVolume(volume + (e.Shift ? 0.01f : 0.05f)); return;
                case Keys.M: ToggleMute(); return;
            }

            if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9)
            {
                int wanted = e.KeyCode - Keys.D0;
                for (int i = 0; i < rows.Count; i++)
                    if (rows[i].Number == wanted) { Choose(i); return; }
            }
            e.Handled = false;
            base.OnKeyDown(e);
        }

        /// <summary>A generous band around the slider, so it is easy to grab.</summary>
        private bool OverSlider(Point p)
        {
            var track = TrackArea;
            return p.Y >= track.Y - 12 && p.Y <= track.Y + track.Height + 12 &&
                   p.X >= track.X - 10 && p.X <= track.Right + 10;
        }

        private void SetVolumeFromX(int x)
        {
            var track = TrackArea;
            SetVolume((float)(x - track.X) / track.Width);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && OverSlider(e.Location))
            {
                dragging = true;
                SetVolumeFromX(e.X);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging)
            {
                SetVolumeFromX(e.X);
                base.OnMouseMove(e);
                return;
            }

            int index = RowAt(e.Y);
            if (index != hover) { hover = index; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hover != -1) { hover = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (SpeakerArea.Contains(e.Location)) { ToggleMute(); return; }
            if (OverSlider(e.Location)) return;   // the slider handled it

            Choose(RowAt(e.Y));
            base.OnMouseClick(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // Over the slider the wheel is volume; anywhere else it walks the list.
            if (OverSlider(PointToClient(MousePosition)))
                SetVolume(volume + (e.Delta > 0 ? 0.03f : -0.03f));
            else
                MoveSelection(e.Delta > 0 ? -1 : 1);
            base.OnMouseWheel(e);
        }

        private void Choose(int index)
        {
            if (index < 0 || index >= rows.Count || rows[index].Header) return;
            Chosen = rows[index].Device;
            Dismiss();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (mixer != null) { mixer.Dispose(); mixer = null; }
                fade.Dispose();
                titleFont.Dispose();
                rowFont.Dispose();
                smallFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal class TrayApp : Form
    {
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        private const int WM_HOTKEY = 0x0312;
        private const int CycleHotkeyId = 0xB00;
        private const int OverlayHotkeyId = 0xAFF;
        private const int FirstDeviceHotkeyId = 0xB01;

        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        // registered hotkey id -> target device id ("" means cycle)
        private readonly Dictionary<int, string> hotkeyTargets = new Dictionary<int, string>();

        public TrayApp()
        {
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            FormBorderStyle = FormBorderStyle.None;
            Opacity = 0;

            tray = new NotifyIcon
            {
                Icon = BuildIcon(),
                Text = App.Name,
                Visible = true,
                ContextMenuStrip = menu
            };
            tray.MouseUp += OnTrayMouseUp;
            menu.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e) { BuildMenu(); };

            ApplyHotkeys(false);

            if (Config.FirstRun)
                BeginInvoke(new MethodInvoker(RunWizard));
        }

        // Set AUDIOHOP_LOG to write %TEMP%\audiohop.log
        private static readonly bool logging = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AUDIOHOP_LOG"));

        private static void Log(string message)
        {
            if (!logging) return;
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "audiohop.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
            }
            catch { }
        }

        private void RunWizard()
        {
            // The language picker restarts the wizard so every label follows along.
            while (true)
            {
                ReleaseHotkeys();   // free the combinations so the wizard can probe them
                using (var wizard = new WizardForm(Audio.GetDevices(false), Audio.GetDevices(true)))
                {
                    wizard.Icon = AppIcon();
                    var result = wizard.ShowDialog();
                    Log("wizard result=" + result);
                    if (result != DialogResult.Retry)
                    {
                        if (result != DialogResult.OK) Config.Save();   // don't ask again
                        break;
                    }
                }
            }
            ApplyHotkeys(true);
        }

        private void ReleaseHotkeys()
        {
            // Touching Handle forces the hidden window into existence, which the
            // hotkey registrations and BeginInvoke both depend on.
            IntPtr window = Handle;
            foreach (var id in hotkeyTargets.Keys)
                UnregisterHotKey(window, id);
            hotkeyTargets.Clear();
        }

        /// <summary>Drops every registration and re-registers what the settings hold.</summary>
        private void ApplyHotkeys(bool reportFailures)
        {
            ReleaseHotkeys();
            var failed = new List<string>();

            if (Config.CycleHotkey.IsSet)
            {
                bool ok = RegisterHotKey(Handle, CycleHotkeyId,
                    Config.CycleHotkey.Modifiers, (int)Config.CycleHotkey.Key);
                Log("register " + Config.CycleHotkey + " (cycle) ok=" + ok);
                if (ok) hotkeyTargets[CycleHotkeyId] = "";
                else failed.Add(Config.CycleHotkey + " (" + L.T("cycle.name") + ")");
            }

            if (Config.OverlayHotkey.IsSet)
            {
                bool ok = RegisterHotKey(Handle, OverlayHotkeyId,
                    Config.OverlayHotkey.Modifiers, (int)Config.OverlayHotkey.Key);
                Log("register " + Config.OverlayHotkey + " (overlay) ok=" + ok);
                if (ok) hotkeyTargets[OverlayHotkeyId] = "#overlay";
                else failed.Add(Config.OverlayHotkey + " (" + L.T("overlay.name") + ")");
            }

            int nextId = FirstDeviceHotkeyId;
            foreach (var pair in Config.DeviceHotkeys)
            {
                if (!pair.Value.IsSet) continue;
                bool ok = RegisterHotKey(Handle, nextId, pair.Value.Modifiers, (int)pair.Value.Key);
                Log("register " + pair.Value + " id=" + nextId + " ok=" + ok);
                if (ok)
                {
                    hotkeyTargets[nextId] = pair.Key;
                    nextId++;
                }
                else failed.Add(pair.Value.ToString());
            }

            if (reportFailures && failed.Count > 0)
                MessageBox.Show(L.T("err.taken", string.Join("\n  ", failed.ToArray())),
                    App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void ShowSettings()
        {
            while (true)
            {
                ReleaseHotkeys();   // so the dialog can probe combinations honestly
                bool again;
                using (var form = new SettingsForm(Audio.GetDevices(false)))
                {
                    form.Icon = AppIcon();
                    var result = form.ShowDialog();
                    again = result == DialogResult.OK && form.LanguageChanged;
                }
                if (!again) break;   // reopen once, in the new language
            }
            ApplyHotkeys(true);
        }

        /// <summary>
        /// The tray icon: just the switch, which still reads at 16 px. Falls
        /// back to a drawn speaker if the resource is missing, so a build
        /// without the artwork still runs.
        /// </summary>
        private static Icon BuildIcon()
        {
            var icon = LoadIcon("tray.ico", SystemInformation.SmallIconSize.Width);
            if (icon != null) return icon;
            return DrawFallbackIcon();
        }

        /// <summary>
        /// The full tile, for window title bars and Alt+Tab. It comes from the
        /// executable's own icon resource, so it is not embedded twice.
        /// </summary>
        public static Icon AppIcon()
        {
            try
            {
                var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return icon;
            }
            catch { }
            return BuildIcon();
        }

        private static Icon LoadIcon(string name, int size)
        {
            try
            {
                using (var stream = System.Reflection.Assembly
                           .GetExecutingAssembly().GetManifestResourceStream(name))
                {
                    if (stream == null) return null;
                    return new Icon(stream, size, size);
                }
            }
            catch { return null; }
        }

        private static Icon DrawFallbackIcon()
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var brush = new SolidBrush(Color.White))
                {
                    // speaker body
                    g.FillRectangle(brush, 5, 12, 6, 8);
                    g.FillPolygon(brush, new[] { new Point(11, 12), new Point(18, 5), new Point(18, 27), new Point(11, 20) });
                }
                using (var pen = new Pen(Color.White, 2.4f))
                {
                    g.DrawArc(pen, 18, 9, 9, 14, -60, 120);
                    g.DrawArc(pen, 20, 4, 15, 24, -60, 120);
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        private void OnTrayMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            BuildMenu();
            typeof(NotifyIcon)
                .GetMethod("ShowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(tray, null);
        }

        private void BuildMenu()
        {
            menu.Items.Clear();
            menu.RightToLeft = L.Rtl ? RightToLeft.Yes : RightToLeft.No;

            AddSection(L.T("menu.output"), Audio.GetDevices(false), true);
            menu.Items.Add(new ToolStripSeparator());
            AddSection(L.T("menu.input"), Audio.GetDevices(true), false);
            menu.Items.Add(new ToolStripSeparator());

            var picker = new ToolStripMenuItem(L.T("menu.overlay"));
            picker.Font = new Font(menu.Font, FontStyle.Bold);
            picker.Click += delegate { BeginInvoke(new MethodInvoker(ToggleOverlay)); };
            menu.Items.Add(picker);
            if (Config.OverlayHotkey.IsSet)
                picker.Text += "   [" + Config.OverlayHotkey + "]";

            menu.Items.Add(new ToolStripSeparator());

            var hotkeys = new ToolStripMenuItem(L.T("menu.hotkeys"));
            hotkeys.Click += delegate { ShowSettings(); };
            menu.Items.Add(hotkeys);

            if (Config.CycleHotkey.IsSet)
                menu.Items.Add(new ToolStripMenuItem("  " + L.T("menu.cycle", Config.CycleHotkey)) { Enabled = false });

            var setup = new ToolStripMenuItem(L.T("menu.wizard"));
            setup.Click += delegate { RunWizard(); };
            menu.Items.Add(setup);

            var startup = new ToolStripMenuItem(L.T("menu.autostart")) { Checked = Autostart.Enabled };
            startup.Click += delegate { Autostart.Enabled = !Autostart.Enabled; };
            menu.Items.Add(startup);

            menu.Items.Add(new ToolStripSeparator());

            var sound = new ToolStripMenuItem(L.T("menu.sound"));
            sound.Click += delegate { OpenUri("ms-settings:sound"); };
            menu.Items.Add(sound);

            var legacy = new ToolStripMenuItem(L.T("menu.classic"));
            legacy.Click += delegate { OpenUri("mmsys.cpl"); };
            menu.Items.Add(legacy);

            menu.Items.Add(new ToolStripSeparator());
            var exit = new ToolStripMenuItem(L.T("menu.exit"));
            exit.Click += delegate { tray.Visible = false; Application.Exit(); };
            menu.Items.Add(exit);
        }

        private void AddSection(string header, List<AudioDevice> devices, bool showHotkeys)
        {
            var title = new ToolStripMenuItem(header) { Enabled = false };
            title.Font = new Font(menu.Font, FontStyle.Bold);
            menu.Items.Add(title);

            if (devices.Count == 0)
            {
                menu.Items.Add(new ToolStripMenuItem("  " + L.T("menu.nodevices")) { Enabled = false });
                return;
            }

            foreach (var d in devices)
            {
                var device = d;

                string suffix = device.Unplugged ? "  " + L.T("menu.unplugged") : "";
                Hotkey assigned;
                if (showHotkeys && Config.DeviceHotkeys.TryGetValue(device.Id, out assigned) && assigned.IsSet)
                    suffix += "   [" + assigned + "]";

                var item = new ToolStripMenuItem("  " + device.Name + suffix)
                {
                    Checked = device.IsDefault,
                    CheckOnClick = false
                };
                item.Click += delegate { Switch(device); };
                menu.Items.Add(item);
            }
        }

        private void Switch(AudioDevice device)
        {
            try
            {
                Audio.SetDefault(device.Id);
                Log("SetDefault ok -> " + device.Name);

                // Windows can accept the call (S_OK) and still keep playing through the
                // old device when the new one is unplugged - check what actually happened.
                if (Audio.IsDefault(device.Id, device.Capture))
                    Notify(L.T("notify.switched"), device.Name);
                else
                    Notify(L.T("notify.nochange"), L.T("notify.nochange.body", device.Name,
                        device.Unplugged ? L.T("notify.unplugged") : L.T("notify.unavailable")));
            }
            catch (Exception ex)
            {
                MessageBox.Show(L.T("notify.failed", ex.Message), App.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Notify(string title, string text)
        {
            tray.BalloonTipTitle = title;
            tray.BalloonTipText = text;
            tray.ShowBalloonTip(1200);
        }

        private static void OpenUri(string uri)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                Log("WM_HOTKEY id=" + m.WParam.ToInt32());
                string target;
                if (hotkeyTargets.TryGetValue(m.WParam.ToInt32(), out target))
                {
                    if (target == "#overlay") ToggleOverlay();
                    else if (target.Length == 0) CycleOutput();
                    else SwitchToId(target);
                    return;
                }
            }
            base.WndProc(ref m);
        }

        private OverlayForm overlay;

        /// <summary>Pressing the hotkey again (or clicking away) closes the picker.</summary>
        private void ToggleOverlay()
        {
            if (overlay != null && !overlay.IsDisposed)
            {
                overlay.Dismiss();
                return;
            }

            var picker = new OverlayForm(Audio.GetDevices(false), Audio.GetDevices(true));
            overlay = picker;
            picker.FormClosed += delegate
            {
                overlay = null;
                var chosen = picker.Chosen;
                picker.Dispose();
                if (chosen != null && !chosen.IsDefault) Switch(chosen);
            };
            picker.Show();
        }

        private void SwitchToId(string deviceId)
        {
            var device = Audio.GetDevices(false)
                .Find(delegate(AudioDevice d) { return d.Id == deviceId; });

            if (device == null)
            {
                Notify(App.Name, L.T("notify.gone"));
                return;
            }
            if (device.IsDefault)
            {
                Notify(L.T("notify.active"), device.Name);
                return;
            }
            Switch(device);
        }

        private void CycleOutput()
        {
            var devices = Audio.GetDevices(false)
                .FindAll(delegate(AudioDevice d) { return !d.Unplugged; });
            if (devices.Count < 2) return;

            int current = devices.FindIndex(delegate(AudioDevice d) { return d.IsDefault; });
            var next = devices[(current + 1) % devices.Count];
            Switch(next);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ReleaseHotkeys();
                tray.Dispose();
            }
            base.Dispose(disposing);
        }

        [STAThread]
        private static void Main()
        {
            bool first;
            using (new System.Threading.Mutex(true, App.Name + "_SingleInstance", out first))
            {
                Config.Load();
                L.Init(Config.Language);

                if (!first)
                {
                    MessageBox.Show(L.T("app.running", App.Name), App.Name);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }
}
