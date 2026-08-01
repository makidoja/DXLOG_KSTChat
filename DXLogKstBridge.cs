// DXLogKstBridge.cs
// DXLog.net custom form for ON4KST using the classic interactive telnet feed.
// This is based on the behaviour found in the dxKst custom form:
//   host www.on4kst.info, port 23000, login prompt, password prompt, room prompt,
//   then /SH US polling for the connected station list.
// Build as x86 .NET Framework class library and copy DLL to %appdata%\DXLog.net\CustomForms.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using DXLog.net;
using DXLogDAL;

[assembly: AssemblyTitle("DXLog KST Chat Bridge")]
[assembly: AssemblyDescription("ON4KST chat, AirScout and DXLog integration")]
[assembly: AssemblyProduct("DXLog KST Chat Bridge")]
[assembly: AssemblyVersion("2.5.1.0")]
[assembly: AssemblyFileVersion("2.5.1.0")]
[assembly: AssemblyInformationalVersion("2.5.1")]

namespace DXLog.net
{
    public class KstChatBridge : KForm
    {
        private const int UserListPanelWidth = 620;
        private const int UserListPanelMinWidth = 600;
        private const int MainWindowMinimumWidth = 1360;
        private const int MainWindowMinimumHeight = 360;
        private const int UserColCall = 0;
        private const int UserColName = 1;
        private const int UserColLocator = 2;
        private const int UserColQtf = 3;
        private const int UserColQrb = 4;
        private const int UserColAirScout = 5;
        private const int UserColActive = 6;
        private const int UserColUnread = 7;
        private const int UserColFirstWorkedBand = 8;

        public static string CusWinName { get { return "KST Chat Bridge"; } }
        public static int CusFormID { get { return 18657; } }

        private Font _windowFont = new Font("Consolas", 9, FontStyle.Regular);
        private Font _boldFont;
        private Font _italicFont;
        private FrmMain _mainForm;
        private ContestData _contestData;
        private TelnetKstClient _kst;
        private KstSettings _settings;

        private TableLayoutPanel _layout;
        private TextBox _hostBox;
        private NumericUpDown _portBox;
        private ComboBox _roomCombo;
        private bool _applyingRoomSelection;
        private ComboBox _distanceCombo;
        private bool _applyingDistanceSelection;
        private ComboBox _airScoutFilterCombo;
        private bool _applyingAirScoutFilterSelection;
        private ComboBox _aircraftFilterCombo;
        private bool _applyingAircraftFilterSelection;
        private CheckBox _airScoutAutoSortCheck;
        private CheckBox _unworkedOnlyCheck;
        private bool _rebuildingVisibleUserList;
        private bool _adjustingUserColumns;
        private string _lastNameHoverCall = "";
        private Button _mapButton;
        private KstUserMapForm _mapForm;
        private TextBox _userBox;
        private TextBox _passBox;
        private Button _setupButton;
        private Button _connectButton;
        private Button _disconnectButton;
        private System.Windows.Forms.Timer _kstLoginTimeoutTimer;
        private bool _kstLoginComplete;
        private bool _kstDisconnectRequested;
        private bool _kstFailureHandling;
        private bool _kstFailurePopupShown;
        private SplitContainer _split;
        private SplitContainer _messageSplit;
        private ListView _users;
        private ListView _messages;
        private ListView _threadMessages;
        private Label _threadHeaderLabel;
        private Button _sendButton;
        private Button _cqButton;
        private TextBox _composeBox;
        private TextBox _composeTargetLabel;
        private ToolTip _macroToolTip;
        private ToolTip _airScoutAlertToolTip;
        private Label _statusLabel;
        private Label _airScoutStatusLabel;
        private AirScoutClient _airScout;
        private System.Windows.Forms.Timer _airScoutRefreshTimer;
        private DateTime _lastAirScoutReplyUtc = DateTime.MinValue;
        private DateTime _lastAirScoutQueryUtc = DateTime.MinValue;
        private string _lastAirScoutQueryCall = "";
        private long _lastAirScoutQueryQrg = 0;
        private readonly Dictionary<string, AirScoutPathResult> _airScoutResults = new Dictionary<string, AirScoutPathResult>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _airScoutScanQueue = new Queue<string>();
        private readonly HashSet<string> _airScoutScanQueuedCalls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _airScoutPendingAutoCall = "";
        private DateTime _airScoutPendingAutoSinceUtc = DateTime.MinValue;
        private DateTime _lastAirScoutFullScanUtc = DateTime.MinValue;
        private int _airScoutScanTotal = 0;
        private int _airScoutScanCompleted = 0;
        private long _airScoutAutoScanQrg = 0;
        private bool _airScoutRescanRequested;
        private string _airScoutUserSnapshotSignature = "";
        private bool _forceUserRefreshAfterCurrent;
        private bool _bandChangeRefreshRunning;
        private readonly object _airScoutPlaneLock = new object();
        private readonly Dictionary<string, AirScoutLivePlane> _airScoutPlaneById = new Dictionary<string, AirScoutLivePlane>(StringComparer.OrdinalIgnoreCase);
        private bool _airScoutPlaneFetchRunning;
        private DateTime _lastAirScoutPlaneFetchUtc = DateTime.MinValue;
        private DateTime _lastGoodAirScoutPlaneFetchUtc = DateTime.MinValue;
        private string _airScoutPlaneFeedStatus = "Aircraft not read";
        private int _airScoutEmptyPlaneFetches;
        private Button[] _macroButtons;
        private System.Windows.Forms.Timer _userRefreshTimer;
        private System.Windows.Forms.Timer _qsoLoggedRefreshTimer;
        private bool _subscribedNewQsoSaved;
        private System.Windows.Forms.Timer _inputFocusTimer;
        private Control _inputFocusTarget;
        private bool _composeInputLocked;
        private ComposeInputMessageFilter _composeInputFilter;
        private BridgeContextMenuMessageFilter _contextMenuFilter;
        private ContextMenuStrip _titleBarContextMenu;
        private ContextMenuStrip _hookedTitleBarContextMenu;
        private ContextMenuStrip _usersContextMenuShield;
        private ContextMenuStrip _messagesContextMenuShield;
        private ContextMenuStrip _threadMessagesContextMenuShield;
        private bool _composeDialogOpen;
        private bool _loadedPersistentColors;
        private bool _handlingLiveFormLayoutChange;
        private bool _restoringWindowBounds;
        private bool _enforcingMinimumWindowSize;
        private System.Windows.Forms.Timer _persistSaveTimer;
        private System.Windows.Forms.Timer _startupBoundsRestoreTimer;
        private bool _startupPositionRestoreDone;
        private bool _allowPositionSave;
        private DateTime _lastUserLayoutChangeUtc;
        private string _lastStyledUserCall = "";
        private ListViewItem _lastStyledMessageItem;
        private ListViewItem _lastStyledThreadItem;
        private System.Windows.Forms.Timer _workedCheckTimer;
        private readonly Queue<string> _workedCheckQueue = new Queue<string>();
        private readonly HashSet<string> _workedCheckQueuedCalls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> _workedBandsByCall = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _watchedCalls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _unreadDirectedByCall = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _lastAirScoutAlertUtcByCall = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private long _perfLastKstRefreshMs;
        private long _perfLastAirScoutReplyMs;
        private long _perfLastPlaneFetchMs;
        private long _perfLastMapRenderMs;
        private long _perfMaxMapRenderMs;
        private long _perfLastUiDelayMs;
        private long _perfMaxUiDelayMs;
        private System.Windows.Forms.Timer _uiLatencyTimer;
        private Stopwatch _uiLatencyWatch;
        private long _uiLatencyExpectedMs;
        private bool _workedBandIndexBuildStarted;
        private bool _workedBandIndexComplete;
        private string _workedBandIndexStatus = "Worked-band index not loaded";

        private readonly Dictionary<string, KstUserInfo> _userMap = new Dictionary<string, KstUserInfo>(StringComparer.OrdinalIgnoreCase);
        private string _lastSelectedCall;
        private string _lastManualStationActionCall = "";
        private DateTime _lastManualStationActionUtc = DateTime.MinValue;
        private bool _refreshingUserList;
        private DateTime _userRefreshStartedUtc = DateTime.MinValue;
        private readonly Dictionary<string, KstUserInfo> _pendingUserSnapshot = new Dictionary<string, KstUserInfo>(StringComparer.OrdinalIgnoreCase);
        private int _userSortColumn = 0;
        private SortOrder _userSortOrder = SortOrder.Ascending;

        public KstChatBridge()
        {
            ConfigureIdentityForDxLog();
            _settings = KstSettings.Load();
            LoadWatchedCallsFromSettings();
            ConfigureColorSet();
            BuildUi();
            FormLayoutChangeEvent += new FormLayoutChange(HandleFormLayoutChangeEvent);
        }

        public KstChatBridge(ContestData cdata)
        {
            ConfigureIdentityForDxLog();
            _contestData = cdata;
            _settings = KstSettings.Load();
            LoadWatchedCallsFromSettings();
            ConfigureColorSet();
            BuildUi();
            FormLayoutChangeEvent += new FormLayoutChange(HandleFormLayoutChangeEvent);
        }

        private void ConfigureIdentityForDxLog()
        {
            // DXLog uses FormID/Name/WindowName when it restores custom-window
            // layout.  Set these before FrmMain reads FormID after constructing
            // the form, otherwise DXLog treats the form like an unknown window
            // and may reset position/title-bar settings when reopened.
            FormID = CusFormID;
            Name = "KstChatBridge";
            WindowName = "KstChatBridge";
            CustomWinMenuName = CusWinName;
            StartPosition = FormStartPosition.Manual;
        }

        private void ConfigureColorSet()
        {
            ColorSetTypes = new string[]
            {
                "Window background",
                "Window text",
                "List background",
                "List text",
                "Selected row background",
                "Selected row text",
                "Direct message background",
                "Direct message text",
                "In log background",
                "In log text",
                "System message background",
                "System message text",
                "Button background",
                "Button text"
            };
            // Keep defaults close to normal DXLog/Windows list colours. Users can still
            // change any of these with the standard DXLog colour menu.
            DefaultColors = new Color[]
            {
                SystemColors.Control,
                SystemColors.ControlText,
                SystemColors.Window,
                SystemColors.WindowText,
                SystemColors.Highlight,
                SystemColors.HighlightText,
                Color.FromArgb(255, 245, 180),
                SystemColors.WindowText,
                SystemColors.Window,
                SystemColors.GrayText,
                SystemColors.ControlLight,
                SystemColors.ControlText,
                SystemColors.Control,
                SystemColors.ControlText
            };
        }

        private void HandleFormLayoutChangeEvent()
        {
            // DXLog has already written the user's new colour/font choices into
            // FormLayout when this event is raised.  Do not inject the previously
            // saved plug-in colours here, otherwise the new colour flashes briefly
            // and is immediately replaced by the old value.
            if (_handlingLiveFormLayoutChange) return;

            _handlingLiveFormLayoutChange = true;
            try
            {
                InitializeLayout();
                // Save the newly applied values immediately, including a Reset to
                // default colours, so they survive closing and reopening the form.
                SavePersistentUiSettings();
            }
            finally
            {
                _handlingLiveFormLayoutChange = false;
            }
        }

        public override void InitializeLayout()
        {
            // On initial/open layout, restore the plug-in's persisted settings.
            // During a live DXLog colour/font menu change, FormLayout already contains
            // the user's new values and must not be overwritten by the old INI values.
            if (!_handlingLiveFormLayoutChange)
                ApplySavedSettingsToFormLayoutBeforeInitialize();

            base.InitializeLayout(_windowFont);
            EnsureTitleBarContextMenuIsolation();

            if (_handlingLiveFormLayoutChange)
                ApplyColoursFromCurrentFormLayout();
            else
                ApplyPersistedColorsOnce();

            if (!String.IsNullOrEmpty(base.FormLayout.FontName) && base.FormLayout.FontSize > 0)
            {
                if (base.FormLayout.FontName.Contains("Courier") || base.FormLayout.FontName.Contains("Consolas"))
                    _windowFont = new Font(base.FormLayout.FontName, base.FormLayout.FontSize, FontStyle.Regular);
                else
                    _windowFont = Helper.GetSpecialFont(FontStyle.Regular, base.FormLayout.FontSize);
            }

            Font = _windowFont;
            if (_boldFont != null) _boldFont.Dispose();
            if (_italicFont != null) _italicFont.Dispose();
            _boldFont = new Font(_windowFont, FontStyle.Bold);
            _italicFont = new Font(_windowFont, FontStyle.Italic);

            if (_users != null) _users.Font = _windowFont;
            if (_messages != null) _messages.Font = _windowFont;
            if (_threadMessages != null) _threadMessages.Font = _windowFont;
            ApplyWindowColors();
            RestyleUsers();
            RestyleMessages();
            RestyleThreadMessages();

            if (_mainForm == null)
            {
                _mainForm = (FrmMain)(ParentForm == null ? Owner : ParentForm);
                if (_mainForm != null)
                {
                    _contestData = _mainForm.ContestDataProvider;
                    if (_contestData != null)
                        _contestData.FocusedRadioChanged += new ContestData.FocusedRadioChange(HandleDxLogFocusChanged);
                    SubscribeDxLogQsoSavedEvent();
                    BeginBuildWorkedBandIndex();
                }
            }

            Text = "KST Chat Bridge";
            UpdateStatus("Ready - ON4KST classic telnet mode, port 23000");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_contestData != null)
                    _contestData.FocusedRadioChanged -= HandleDxLogFocusChanged;
                UnsubscribeDxLogQsoSavedEvent();
                if (_userRefreshTimer != null)
                {
                    _userRefreshTimer.Stop();
                    _userRefreshTimer.Dispose();
                }
                if (_qsoLoggedRefreshTimer != null)
                {
                    _qsoLoggedRefreshTimer.Stop();
                    _qsoLoggedRefreshTimer.Dispose();
                }
                if (_workedCheckTimer != null)
                {
                    _workedCheckTimer.Stop();
                    _workedCheckTimer.Dispose();
                }
                if (_uiLatencyTimer != null)
                {
                    _uiLatencyTimer.Stop();
                    _uiLatencyTimer.Dispose();
                }
                if (_inputFocusTimer != null)
                {
                    _inputFocusTimer.Stop();
                    _inputFocusTimer.Dispose();
                }
                if (_composeInputFilter != null)
                {
                    try { Application.RemoveMessageFilter(_composeInputFilter); } catch { }
                    _composeInputFilter = null;
                }
                if (_contextMenuFilter != null)
                {
                    try { Application.RemoveMessageFilter(_contextMenuFilter); } catch { }
                    _contextMenuFilter = null;
                }
                if (_persistSaveTimer != null)
                {
                    _persistSaveTimer.Stop();
                    _persistSaveTimer.Dispose();
                }
                if (_startupBoundsRestoreTimer != null)
                {
                    _startupBoundsRestoreTimer.Stop();
                    _startupBoundsRestoreTimer.Dispose();
                }
                if (_airScoutRefreshTimer != null)
                {
                    _airScoutRefreshTimer.Stop();
                    _airScoutRefreshTimer.Dispose();
                }
                if (_airScout != null)
                {
                    _airScout.Dispose();
                    _airScout = null;
                }
                if (_kst != null)
                    _kst.Dispose();
                if (_boldFont != null)
                    _boldFont.Dispose();
                if (_italicFont != null)
                    _italicFont.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnActivated(EventArgs e)
        {
            // Do not call KForm.OnActivated(). KForm returns focus to DXLog's QSO line,
            // which is useful for read-only windows but wrong for KST message dialogs.
            // Also do not re-apply saved window bounds here: doing so makes the window
            // snap back while the operator is trying to move it.
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            BeginInvoke(new MethodInvoker(delegate { StartStartupBoundsRestoreTimer(); }));
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            BeginInvoke(new MethodInvoker(delegate { StartStartupBoundsRestoreTimer(); }));
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
                BeginInvoke(new MethodInvoker(delegate { StartStartupBoundsRestoreTimer(); }));
            else
                SavePersistentUiSettings();
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            SaveLayoutAfterUserMoveOrResize();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            EnforceMinimumWindowSize();
            SaveLayoutAfterUserMoveOrResize();
        }

        private void EnforceMinimumWindowSize()
        {
            try
            {
                if (_enforcingMinimumWindowSize || WindowState != FormWindowState.Normal) return;
                int requiredWidth = Math.Max(MainWindowMinimumWidth, MinimumSize.Width);
                int requiredHeight = Math.Max(MainWindowMinimumHeight, MinimumSize.Height);
                if (Width >= requiredWidth && Height >= requiredHeight) return;

                _enforcingMinimumWindowSize = true;
                Size = new Size(Math.Max(Width, requiredWidth), Math.Max(Height, requiredHeight));
            }
            finally
            {
                _enforcingMinimumWindowSize = false;
            }
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            SaveLayoutAfterUserMoveOrResize();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            SaveLayoutAfterUserMoveOrResize();
        }

        private void SaveLayoutAfterUserMoveOrResize()
        {
            try
            {
                if (_restoringWindowBounds) return;
                if (!_allowPositionSave) return;
                if (!IsHandleCreated || !Visible) return;
                if (Width < 100 || Height < 100) return;
                RememberCurrentWindowBounds();
                _lastUserLayoutChangeUtc = DateTime.UtcNow;
                SchedulePersistentSave();
            }
            catch { }
        }

        private void BuildUi()
        {
            MinimumSize = new Size(MainWindowMinimumWidth, MainWindowMinimumHeight);
            if (Width < 1180) Width = 1180;
            if (Height < 460) Height = 460;

            _layout = new TableLayoutPanel();
            _layout.Dock = DockStyle.Fill;
            _layout.ColumnCount = 12;
            _layout.RowCount = 4;
            _layout.Padding = new Padding(6);
            _layout.ColumnStyles.Clear();
            for (int i = 0; i < 12; i++) _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 8.3333f));
            int topBarHeight = Math.Max(62, Font.Height + 38);
            _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, topBarHeight));
            _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

            // Host/port/user/pass are deliberately configured in Setup only.  The boxes
            // still exist internally so the existing connect/settings code can remain simple.
            _hostBox = new TextBox { Text = _settings.Host };
            _portBox = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = _settings.Port };
            _userBox = new TextBox { Text = _settings.Callsign };
            _passBox = new TextBox { Text = _settings.Password, UseSystemPasswordChar = true };

            _setupButton = new Button { Text = "Setup", Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(3) };
            _connectButton = new Button { Text = "Connect", Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(3) };
            _disconnectButton = new Button { Text = "Disconnect", Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(3), Enabled = false };
            _mapButton = new Button { Text = "Map", Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(3) };
            Size topBarControlMinimum = new Size(0, Math.Max(24, Font.Height + 10));
            _setupButton.MinimumSize = topBarControlMinimum;
            _connectButton.MinimumSize = topBarControlMinimum;
            _disconnectButton.MinimumSize = topBarControlMinimum;
            _mapButton.MinimumSize = topBarControlMinimum;
            _roomCombo = new ComboBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Standard,
                IntegralHeight = false,
                DropDownHeight = 280,
                TabStop = true,
                Margin = new Padding(3)
            };
            PopulateRoomCombo(_roomCombo, _settings.Room);

            _distanceCombo = new ComboBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Standard,
                IntegralHeight = true,
                TabStop = true,
                Margin = new Padding(3)
            };
            PopulateDistanceCombo(_distanceCombo, _settings.DistanceFilterKm);

            _airScoutFilterCombo = new ComboBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Standard,
                IntegralHeight = true,
                TabStop = true,
                Margin = new Padding(3)
            };
            PopulateAirScoutFilterCombo(_airScoutFilterCombo, _settings.AirScoutFilterMinutes);

            _aircraftFilterCombo = new ComboBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Standard,
                IntegralHeight = true,
                TabStop = true,
                Margin = new Padding(3)
            };
            PopulateAircraftFilterCombo(_aircraftFilterCombo, _settings.AircraftFilterMode);
            _roomCombo.MinimumSize = topBarControlMinimum;
            _distanceCombo.MinimumSize = topBarControlMinimum;
            _airScoutFilterCombo.MinimumSize = topBarControlMinimum;
            _aircraftFilterCombo.MinimumSize = topBarControlMinimum;

            _airScoutAutoSortCheck = new CheckBox
            {
                Text = "Auto sort",
                Checked = _settings.AirScoutAutoSort,
                Anchor = AnchorStyles.Left,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = true,
                Margin = new Padding(3, 0, 3, 0)
            };
            _unworkedOnlyCheck = new CheckBox
            {
                Text = "Need",
                Checked = _settings.UnworkedCurrentBandOnly,
                Anchor = AnchorStyles.Left,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = true,
                Margin = new Padding(3, 0, 3, 0)
            };

            // Keep related controls together in clearly separated DXLog-style groups.
            // Each group uses the same height, border, internal spacing and alignment.
            TableLayoutPanel headerPanel = new TableLayoutPanel();
            headerPanel.Dock = DockStyle.Fill;
            headerPanel.Margin = new Padding(0);
            headerPanel.Padding = new Padding(0);
            headerPanel.ColumnCount = 5;
            headerPanel.RowCount = 1;
            headerPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            GroupBox settingsGroup = new GroupBox { Text = "Settings", Dock = DockStyle.Fill, Margin = new Padding(4, 2, 4, 2), Padding = new Padding(6, 4, 6, 6) };
            TableLayoutPanel settingsLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1, Margin = new Padding(0), Padding = new Padding(0) };
            settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _setupButton.Dock = DockStyle.Fill;
            _setupButton.Margin = new Padding(2);
            settingsLayout.Controls.Add(_setupButton, 0, 0);
            settingsGroup.Controls.Add(settingsLayout);

            GroupBox mapDistanceGroup = new GroupBox { Text = "Map / Distance", Dock = DockStyle.Fill, Margin = new Padding(4, 2, 4, 2), Padding = new Padding(6, 4, 6, 6) };
            TableLayoutPanel mapDistanceLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), Padding = new Padding(0) };
            mapDistanceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            mapDistanceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            mapDistanceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _mapButton.Dock = DockStyle.Fill;
            _mapButton.Margin = new Padding(2);
            _distanceCombo.Dock = DockStyle.Fill;
            _distanceCombo.Margin = new Padding(2);
            mapDistanceLayout.Controls.Add(_mapButton, 0, 0);
            mapDistanceLayout.Controls.Add(_distanceCombo, 1, 0);
            mapDistanceGroup.Controls.Add(mapDistanceLayout);

            GroupBox airScoutGroup = new GroupBox { Text = "AirScout", Dock = DockStyle.Fill, Margin = new Padding(4, 2, 4, 2), Padding = new Padding(6, 4, 6, 6) };
            TableLayoutPanel airScoutLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), Padding = new Padding(0) };
            airScoutLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            airScoutLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            airScoutLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _airScoutFilterCombo.Dock = DockStyle.Fill;
            _airScoutFilterCombo.Margin = new Padding(2);
            _aircraftFilterCombo.Dock = DockStyle.Fill;
            _aircraftFilterCombo.Margin = new Padding(2);
            airScoutLayout.Controls.Add(_airScoutFilterCombo, 0, 0);
            airScoutLayout.Controls.Add(_aircraftFilterCombo, 1, 0);
            airScoutGroup.Controls.Add(airScoutLayout);

            GroupBox listFiltersGroup = new GroupBox { Text = "List filters", Dock = DockStyle.Fill, Margin = new Padding(4, 2, 4, 2), Padding = new Padding(6, 4, 6, 6) };
            TableLayoutPanel listFiltersLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), Padding = new Padding(0) };
            listFiltersLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            listFiltersLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            listFiltersLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _airScoutAutoSortCheck.Dock = DockStyle.None;
            _airScoutAutoSortCheck.Anchor = AnchorStyles.Left;
            _airScoutAutoSortCheck.AutoSize = true;
            _airScoutAutoSortCheck.CheckAlign = ContentAlignment.MiddleLeft;
            _airScoutAutoSortCheck.TextAlign = ContentAlignment.MiddleLeft;
            _airScoutAutoSortCheck.Margin = new Padding(8, 0, 2, 0);
            _unworkedOnlyCheck.Dock = DockStyle.None;
            _unworkedOnlyCheck.Anchor = AnchorStyles.Left;
            _unworkedOnlyCheck.AutoSize = true;
            _unworkedOnlyCheck.CheckAlign = ContentAlignment.MiddleLeft;
            _unworkedOnlyCheck.TextAlign = ContentAlignment.MiddleLeft;
            _unworkedOnlyCheck.Margin = new Padding(6, 0, 2, 0);
            listFiltersLayout.Controls.Add(_airScoutAutoSortCheck, 0, 0);
            listFiltersLayout.Controls.Add(_unworkedOnlyCheck, 1, 0);
            listFiltersGroup.Controls.Add(listFiltersLayout);

            GroupBox connectionGroup = new GroupBox { Text = "KST Chat", Dock = DockStyle.Fill, Margin = new Padding(4, 2, 4, 2), Padding = new Padding(6, 4, 6, 6) };
            TableLayoutPanel connectionLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0), Padding = new Padding(0) };
            connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
            connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            connectionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _roomCombo.Dock = DockStyle.Fill;
            _roomCombo.Margin = new Padding(2);
            _connectButton.Dock = DockStyle.Fill;
            _connectButton.Margin = new Padding(2);
            _disconnectButton.Dock = DockStyle.Fill;
            _disconnectButton.Margin = new Padding(2);
            connectionLayout.Controls.Add(_roomCombo, 0, 0);
            connectionLayout.Controls.Add(_connectButton, 1, 0);
            connectionLayout.Controls.Add(_disconnectButton, 2, 0);
            connectionGroup.Controls.Add(connectionLayout);

            headerPanel.Controls.Add(settingsGroup, 0, 0);
            headerPanel.Controls.Add(mapDistanceGroup, 1, 0);
            headerPanel.Controls.Add(airScoutGroup, 2, 0);
            headerPanel.Controls.Add(listFiltersGroup, 3, 0);
            headerPanel.Controls.Add(connectionGroup, 4, 0);
            _layout.Controls.Add(headerPanel, 0, 0); _layout.SetColumnSpan(headerPanel, 12);

            _split = new SplitContainer();
            _split.Dock = DockStyle.Fill;
            _split.Orientation = Orientation.Vertical;
            _split.FixedPanel = FixedPanel.Panel1;
            _split.IsSplitterFixed = true;
            // Start with deliberately small safe min sizes. DXLog creates custom
            // forms before the final client size is known; setting a 540 px
            // SplitterDistance here can crash if the temporary SplitContainer
            // width is still tiny. ApplySplitSize() sets the real width once the
            // form is shown/resized.
            _split.Panel1MinSize = 50;
            _split.Panel2MinSize = 50;
            _split.SizeChanged += delegate { ApplySplitSize(); };

            _users = new BufferedListView();
            _users.Dock = DockStyle.Fill;
            _users.View = View.Details;
            _users.FullRowSelect = true;
            _users.MultiSelect = false;
            _users.GridLines = false;
            _users.HideSelection = false;
            _users.OwnerDraw = true;
            _users.DrawColumnHeader += DrawListColumnHeader;
            _users.DrawSubItem += DrawListSubItem;
            _users.ColumnClick += UsersColumnClick;
            _users.ColumnWidthChanging += UsersColumnWidthChanging;
            _users.Resize += delegate { AdjustUserColumns(); };
            RebuildUserColumns();
            _users.ShowItemToolTips = true;
            InstallContextMenuShield(_users, ref _usersContextMenuShield);
            _users.SelectedIndexChanged += delegate { UsersSelectedIndexChanged(); };
            _users.MouseClick += UsersMouseClick;
            _users.MouseMove += UsersMouseMove;
            _users.MouseLeave += delegate
            {
                _lastNameHoverCall = "";
                if (_macroToolTip != null) _macroToolTip.Hide(_users);
            };

            _messageSplit = new SplitContainer();
            _messageSplit.Dock = DockStyle.Fill;
            _messageSplit.Orientation = Orientation.Horizontal;
            _messageSplit.Panel1MinSize = 120;
            _messageSplit.Panel2MinSize = 90;
            _messageSplit.SplitterWidth = 5;
            _messageSplit.SizeChanged += delegate { ApplyMessageSplitSize(); };

            _messages = CreateMessageListView();
            _messages.DoubleClick += delegate { PutSelectedMessageIntoDxLog(); };
            InstallContextMenuShield(_messages, ref _messagesContextMenuShield);
            _messages.SelectedIndexChanged += delegate { MessagesSelectedIndexChanged(); };

            Panel threadPanel = new Panel();
            threadPanel.Dock = DockStyle.Fill;
            _threadHeaderLabel = new Label { Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Text = "Selected station messages", Padding = new Padding(4, 0, 0, 0) };
            _threadMessages = CreateMessageListView();
            _threadMessages.DoubleClick += delegate { PutSelectedThreadMessageIntoDxLog(); };
            InstallContextMenuShield(_threadMessages, ref _threadMessagesContextMenuShield);
            _threadMessages.SelectedIndexChanged += delegate { ThreadMessagesSelectedIndexChanged(); };
            threadPanel.Controls.Add(_threadMessages);
            threadPanel.Controls.Add(_threadHeaderLabel);

            _messageSplit.Panel1.Controls.Add(_messages);
            _messageSplit.Panel2.Controls.Add(threadPanel);

            _split.Panel1.Controls.Add(_users);
            _split.Panel2.Controls.Add(_messageSplit);
            _layout.Controls.Add(_split, 0, 1); _layout.SetColumnSpan(_split, 12);

            TableLayoutPanel actionPanel = new TableLayoutPanel();
            actionPanel.Dock = DockStyle.Fill;
            actionPanel.Margin = new Padding(0);
            actionPanel.Padding = new Padding(0);
            actionPanel.RowCount = 1;
            actionPanel.ColumnCount = 9;
            actionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            actionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            actionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            for (int i = 0; i < 4; i++) actionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            actionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 2));

            _sendButton = new Button { Text = "CQ", Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(3), Enabled = false };
            _composeTargetLabel = new TextBox { Text = "CQ", Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(3), ReadOnly = true, TabStop = false };
            _composeBox = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(3), Enabled = false };
            _cqButton = new Button { Text = "Send", Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(3), Enabled = false };
            actionPanel.Controls.Add(_sendButton, 0, 0);
            actionPanel.Controls.Add(_composeTargetLabel, 1, 0);
            actionPanel.Controls.Add(_composeBox, 2, 0);
            actionPanel.Controls.Add(_cqButton, 3, 0);

            _macroToolTip = new ToolTip { AutoPopDelay = 12000, InitialDelay = 250, ReshowDelay = 100, ShowAlways = true };
            _macroToolTip.SetToolTip(_airScoutFilterCombo, "Show all stations, NOW only, or opportunities within the selected time");
            _macroToolTip.SetToolTip(_aircraftFilterCombo, "Limit AirScout opportunities and map aircraft by AirScout size or classified passenger/cargo type");
            _macroToolTip.SetToolTip(_airScoutAutoSortCheck, "Automatically keep NOW and approaching stations at the top of the list");
            _macroToolTip.SetToolTip(_unworkedOnlyCheck, "Show only stations not yet worked on the current DXLog band; combines with AS and distance filters");
            _airScoutAlertToolTip = new ToolTip { AutoPopDelay = 6000, InitialDelay = 0, ReshowDelay = 0, ShowAlways = true, IsBalloon = true, ToolTipTitle = "AirScout opportunity" };
            _macroButtons = new Button[4];
            for (int i = 0; i < _macroButtons.Length; i++)
            {
                int macroIndex = i;
                _macroButtons[i] = new Button { Text = "M" + (i + 1).ToString(), Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(3), Enabled = false, MinimumSize = new Size(54, 24) };
                _macroButtons[i].Click += async delegate { await SendMacroClicked(macroIndex); };
                _macroButtons[i].MouseEnter += delegate { UpdateMacroToolTip(macroIndex); };
                _macroButtons[i].MouseUp += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Right) EditMacrosClicked(macroIndex); };
                actionPanel.Controls.Add(_macroButtons[i], 4 + i, 0);
            }
            _layout.Controls.Add(actionPanel, 0, 2); _layout.SetColumnSpan(actionPanel, 12);

            TableLayoutPanel statusPanel = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0), ColumnCount = 2, RowCount = 1 };
            statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            _statusLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            _statusLabel.Cursor = Cursors.Hand;
            _statusLabel.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right) ShowPerformanceDiagnostics();
            };
            _airScoutStatusLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Text = "AirScout: Off" };
            statusPanel.Controls.Add(_statusLabel, 0, 0);
            statusPanel.Controls.Add(_airScoutStatusLabel, 1, 0);
            _layout.Controls.Add(statusPanel, 0, 3); _layout.SetColumnSpan(statusPanel, 12);

            // Keep help available without adding more permanent text to the compact
            // DXLog-style layout. Item-specific station tooltips still take priority
            // over these general control descriptions.
            _macroToolTip.SetToolTip(_setupButton, "Open KST, station, worked-band and AirScout settings");
            _macroToolTip.SetToolTip(_mapButton, "Open the KST station and AirScout map");
            _macroToolTip.SetToolTip(_distanceCombo, "Filter stations by distance from your QTH locator");
            _macroToolTip.SetToolTip(_airScoutFilterCombo, "Show all stations, NOW only, or opportunities within the selected time");
            _macroToolTip.SetToolTip(_aircraftFilterCombo, "All; AirScout XX/XXX; XXX only; classified passenger/cargo; or classified cargo only");
            _macroToolTip.SetToolTip(_airScoutAutoSortCheck, "Keep NOW and approaching AirScout opportunities at the top of the station list");
            _macroToolTip.SetToolTip(_unworkedOnlyCheck, "Show stations not yet worked on the current DXLog band; watched stations remain visible");
            _macroToolTip.SetToolTip(_roomCombo, "Select the ON4KST room; the internal room number is hidden");
            _macroToolTip.SetToolTip(_connectButton, "Connect to the selected ON4KST room");
            _macroToolTip.SetToolTip(_disconnectButton, "Disconnect from ON4KST");

            _kstLoginTimeoutTimer = new System.Windows.Forms.Timer();
            _kstLoginTimeoutTimer.Interval = 20000;
            _kstLoginTimeoutTimer.Tick += delegate
            {
                _kstLoginTimeoutTimer.Stop();
                if (!_kstLoginComplete && !_kstDisconnectRequested)
                {
                    HandleKstConnectionFailure(
                        "KST login timed out",
                        "ON4KST did not complete the login within 20 seconds.\r\n\r\nCheck the selected room, callsign, password and internet connection, then try Connect again.");
                }
            };
            _macroToolTip.SetToolTip(_messages, "Current room messages. Select or double-click a message to work with its sender");
            _macroToolTip.SetToolTip(_threadHeaderLabel, "Messages involving the station selected manually in the user list");
            _macroToolTip.SetToolTip(_threadMessages, "Messages involving the selected station. Incoming messages do not change the selected map path");
            _macroToolTip.SetToolTip(_sendButton, "Clear the directed target and return to CQ messaging");
            _macroToolTip.SetToolTip(_composeTargetLabel, "Current message target: CQ or the selected callsign");
            _macroToolTip.SetToolTip(_composeBox, "Type a custom KST message; press Enter or click Send");
            _macroToolTip.SetToolTip(_cqButton, "Send the custom message to the displayed target");
            _macroToolTip.SetToolTip(_statusLabel, "Bridge status. Right-click for performance and UI-latency diagnostics");
            _macroToolTip.SetToolTip(_airScoutStatusLabel, "AirScout connection, scan progress and aircraft-feed health");

            Controls.Add(_layout);

            // Save our own settings as well as DXLog's FormLayout.  DXLog does not
            // always persist custom-form layout/colours immediately when a custom
            // window is closed and reopened, so this plugin keeps its own INI too.
            FormClosing += delegate { CloseMapWindow(); SavePersistentUiSettings(); };
            FormClosed += delegate { CloseMapWindow(); SavePersistentUiSettings(); };
            HandleDestroyed += delegate { CloseMapWindow(); SavePersistentUiSettings(); };
            VisibleChanged += delegate
            {
                if (!Visible)
                {
                    CloseMapWindow();
                    SavePersistentUiSettings();
                }
            };
            if (ContextMenuStrip != null)
                ContextMenuStrip.Closed += delegate { SavePersistentUiSettings(); };

            Move += delegate { SaveLayoutAfterUserMoveOrResize(); };
            Resize += delegate { SaveLayoutAfterUserMoveOrResize(); };

            _setupButton.Click += delegate { SetupClicked(); };
            _distanceCombo.SelectionChangeCommitted += delegate { if (!_applyingDistanceSelection) ApplyDistanceFilterSelection(); };
            _airScoutFilterCombo.SelectionChangeCommitted += delegate { if (!_applyingAirScoutFilterSelection) ApplyAirScoutFilterSelection(); };
            _aircraftFilterCombo.SelectionChangeCommitted += delegate { if (!_applyingAircraftFilterSelection) ApplyAircraftFilterSelection(); };
            _airScoutAutoSortCheck.CheckedChanged += delegate
            {
                if (_settings == null) return;
                _settings.AirScoutAutoSort = _airScoutAutoSortCheck.Checked;
                _settings.Save();
                if (_settings.AirScoutAutoSort) SortUsersByAirScoutOpportunity();
            };
            _unworkedOnlyCheck.CheckedChanged += delegate
            {
                if (_settings == null) return;
                _settings.UnworkedCurrentBandOnly = _unworkedOnlyCheck.Checked;
                _settings.Save();
                RebuildVisibleUserList();
                UpdateStatus(_settings.UnworkedCurrentBandOnly ? "Filter: unworked on current band" : "Filter: all worked states");
            };
            _roomCombo.SelectionChangeCommitted += async delegate { if (!_applyingRoomSelection) await ChangeRoomClicked(); };

            // DXLog can reclaim focus before a ComboBox opens. Explicitly open both
            // selectors on mouse click and the normal keyboard shortcuts so their
            // drop-down lists remain reliable while the bridge is hosted inside DXLog.
            HookReliableDropDown(_distanceCombo);
            HookReliableDropDown(_airScoutFilterCombo);
            HookReliableDropDown(_aircraftFilterCombo);
            HookReliableDropDown(_roomCombo);
            _mapButton.Click += delegate { ShowMapWindow(); };
            _connectButton.Click += async delegate { await ConnectClicked(); };
            _disconnectButton.Click += delegate { DisconnectClicked(); };
            _sendButton.Click += delegate { PrepareCqCompose(); };
            _cqButton.Click += async delegate { await SendComposeClicked(); };
            _composeBox.KeyDown += async delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    await SendComposeClicked();
                }
            };
            HookEditableFocus(_composeBox);
            _composeInputFilter = new ComposeInputMessageFilter(ShouldCaptureComposeInput, GetComposeInputHandle, ReleaseComposeInputCapture);
            Application.AddMessageFilter(_composeInputFilter);

            // KForm attaches its colour/options menu to the whole custom window.
            // Intercept WM_CONTEXTMENU so that menu is available only over the
            // DXLog title strip. Child controls retain their own right-click actions.
            _contextMenuFilter = new BridgeContextMenuMessageFilter(this);
            Application.AddMessageFilter(_contextMenuFilter);

            _persistSaveTimer = new System.Windows.Forms.Timer();
            _persistSaveTimer.Interval = 500;
            _persistSaveTimer.Tick += delegate
            {
                _persistSaveTimer.Stop();
                SavePersistentUiSettings();
            };

            _startupBoundsRestoreTimer = new System.Windows.Forms.Timer();
            _startupBoundsRestoreTimer.Interval = 750;
            _startupBoundsRestoreTimer.Tick += delegate
            {
                _startupBoundsRestoreTimer.Stop();
                ForceApplySavedLayout();
                _startupPositionRestoreDone = true;
                _allowPositionSave = true;
            };

            HookTitleBarMenuPersistence();

            _inputFocusTimer = new System.Windows.Forms.Timer();
            _inputFocusTimer.Interval = 125;
            _inputFocusTimer.Tick += delegate
            {
                if (!_composeInputLocked || _inputFocusTarget == null || _inputFocusTarget.IsDisposed)
                {
                    _inputFocusTimer.Stop();
                    _inputFocusTarget = null;
                    return;
                }
                try
                {
                    if (_inputFocusTarget.Enabled && _inputFocusTarget.Visible && !_inputFocusTarget.ContainsFocus && _inputFocusTarget.CanFocus)
                    {
                        Activate();
                        ActiveControl = _inputFocusTarget;
                        _inputFocusTarget.Select();
                    }
                }
                catch { }
            };

            _userRefreshTimer = new System.Windows.Forms.Timer();
            _userRefreshTimer.Interval = 10000;
            _userRefreshTimer.Tick += async delegate { await RefreshUsers(); };

            // When a QSO is logged in DXLog, refresh immediately rather than
            // waiting for the normal 10 second ON4KST /SH US poll.  The small
            // timer debounce avoids sending multiple refresh commands if DXLog
            // fires more than once while the log line is being finalized.
            _qsoLoggedRefreshTimer = new System.Windows.Forms.Timer();
            _qsoLoggedRefreshTimer.Interval = 1000;
            _qsoLoggedRefreshTimer.Tick += async delegate
            {
                _qsoLoggedRefreshTimer.Stop();
                await ForceRefreshAfterQsoLoggedAsync();
            };

            // DXLog duplicate checks can touch the contest log. Process at most one
            // station per timer tick so a large KST room never causes a long UI pause.
            _workedCheckTimer = new System.Windows.Forms.Timer();
            _workedCheckTimer.Interval = 75;
            _workedCheckTimer.Tick += delegate { ProcessNextWorkedCheck(); };

            _airScoutRefreshTimer = new System.Windows.Forms.Timer();
            // Auto-scan one KST station at a time. AirScout normally replies quickly,
            // so a short UI timer lets the whole list populate without flooding UDP.
            _airScoutRefreshTimer.Interval = 500;
            _airScoutRefreshTimer.Tick += delegate
            {
                RunAirScoutAutoScanTick();
                UpdateAirScoutStatusLabel();
            };
            ConfigureAirScoutClient();

            _uiLatencyWatch = Stopwatch.StartNew();
            _uiLatencyExpectedMs = _uiLatencyWatch.ElapsedMilliseconds + 250;
            _uiLatencyTimer = new System.Windows.Forms.Timer();
            _uiLatencyTimer.Interval = 250;
            _uiLatencyTimer.Tick += delegate
            {
                long now = _uiLatencyWatch == null ? 0 : _uiLatencyWatch.ElapsedMilliseconds;
                long delay = Math.Max(0, now - _uiLatencyExpectedMs);
                _perfLastUiDelayMs = delay;
                if (delay > _perfMaxUiDelayMs) _perfMaxUiDelayMs = delay;
                _uiLatencyExpectedMs = now + _uiLatencyTimer.Interval;
            };
            _uiLatencyTimer.Start();

            AdjustUserColumns();
            AdjustMessageColumns();
        }

        private ListView CreateMessageListView()
        {
            ListView lv = new BufferedListView();
            lv.Dock = DockStyle.Fill;
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.GridLines = false;
            lv.HideSelection = false;
            lv.OwnerDraw = true;
            lv.DrawColumnHeader += DrawListColumnHeader;
            lv.DrawSubItem += DrawListSubItem;
            lv.Resize += delegate { AdjustMessageColumns(); };
            lv.Columns.Add("UTC", 70);
            lv.Columns.Add("From", 90);
            lv.Columns.Add("Name", 120);
            lv.Columns.Add("Message", 700);
            return lv;
        }

        private void ApplyMessageSplitSize()
        {
            if (_messageSplit == null) return;
            try
            {
                int h = _messageSplit.ClientSize.Height;
                if (h < 240) return;
                int bottom = Math.Max(120, Math.Min(220, h / 3));
                int distance = Math.Max(_messageSplit.Panel1MinSize, h - bottom - _messageSplit.SplitterWidth);
                int maxDistance = h - _messageSplit.Panel2MinSize - _messageSplit.SplitterWidth;
                if (distance > maxDistance) distance = maxDistance;
                if (distance >= _messageSplit.Panel1MinSize && distance <= maxDistance)
                    _messageSplit.SplitterDistance = distance;
            }
            catch { }
        }

        private void ForceApplySavedLayout()
        {
            try
            {
                // Apply saved position/size once, slightly after DXLog has created
                // and positioned the MDI child. Applying too early is overwritten
                // by DXLog; applying repeatedly causes snap-back while dragging.
                ApplySavedSettingsToFormLayoutBeforeInitialize();
                ApplyTitleBarColorFromSettings();
                ApplySavedWindowBounds();
                ApplySplitSize();
            }
            catch { }
        }

        private void ApplyTitleBarColorFromSettings()
        {
            try
            {
                if (_settings == null || String.IsNullOrWhiteSpace(_settings.TitleBarColor)) return;
                string n = NormaliseTitleBarColourNumber(_settings.TitleBarColor);
                if (String.IsNullOrEmpty(n)) return;

                DALHeader.StructFormLayout fml = FormLayout;
                fml.TitleBarColor = n;
                FormLayout = fml;

                string colourName = TitleBarColourNameFromNumber(n);
                MethodInfo mi = typeof(KForm).GetMethod("SetTitleBarColorClickHandler", BindingFlags.Instance | BindingFlags.NonPublic);
                if (mi != null && !String.IsNullOrEmpty(colourName))
                    mi.Invoke(this, new object[] { colourName, n });

                try { Invalidate(true); } catch { }
            }
            catch { }
        }

        private string ReadCurrentTitleBarColourNumber()
        {
            // Prefer KForm.FormLayout.TitleBarColor because the DXLog title-bar
            // colour menu writes directly to that field.  The public TitleBarColor
            // property falls back to Red when nothing has been applied yet, which
            // was overwriting the saved user choice.
            try
            {
                if (!String.IsNullOrWhiteSpace(FormLayout.TitleBarColor))
                    return NormaliseTitleBarColourNumber(FormLayout.TitleBarColor);
            }
            catch { }
            try
            {
                int n = TitleBarColor;
                if (n >= 1 && n <= 6) return n.ToString();
            }
            catch { }
            return "";
        }

        private static string NormaliseTitleBarColourNumber(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "";
            value = value.Trim();
            int n;
            if (Int32.TryParse(value, out n) && n >= 1 && n <= 6) return n.ToString();
            switch (value.ToLowerInvariant())
            {
                case "red": return "1";
                case "green": return "2";
                case "orange": return "3";
                case "purple": return "4";
                case "gray":
                case "grey": return "5";
                case "yellow": return "6";
                default: return "";
            }
        }

        private static string TitleBarColourNameFromNumber(string value)
        {
            switch (NormaliseTitleBarColourNumber(value))
            {
                case "1": return "Red";
                case "2": return "Green";
                case "3": return "Orange";
                case "4": return "Purple";
                case "5": return "Gray";
                case "6": return "Yellow";
                default: return "";
            }
        }

        private void ApplySavedSettingsToFormLayoutBeforeInitialize()
        {
            try
            {
                if (_settings == null) return;

                DALHeader.StructFormLayout fml = FormLayout;

                if (_settings.HasWindowBounds)
                {
                    fml.LocX = SafeShort(_settings.WindowX);
                    fml.LocY = SafeShort(_settings.WindowY);
                    fml.Width = SafeShort(_settings.WindowW);
                    fml.Height = SafeShort(_settings.WindowH);
                }

                if (!String.IsNullOrEmpty(_settings.TitleBarColor))
                    fml.TitleBarColor = _settings.TitleBarColor;

                if (_settings.ColorValues != null && _settings.ColorValues.Length > 0)
                {
                    if (fml.ColorFlags == null || fml.ColorFlags.Length < 20)
                        fml.ColorFlags = new int[20];

                    for (int i = 0; i < _settings.ColorValues.Length && i < fml.ColorFlags.Length; i++)
                    {
                        int argb = _settings.ColorValues[i];
                        if (argb != 0)
                            fml.ColorFlags[i] = argb;
                    }
                }

                FormLayout = fml;
            }
            catch { }
        }

        private void ApplyPersistedColorsOnce()
        {
            if (_loadedPersistentColors) return;
            _loadedPersistentColors = true;

            try
            {
                if (_settings == null || _settings.ColorValues == null || _settings.ColorValues.Length == 0) return;
                if (ColorSetTypes == null || ColorValues == null) return;

                int max = Math.Min(ColorValues.Length, _settings.ColorValues.Length);
                for (int i = 0; i < max; i++)
                {
                    int argb = _settings.ColorValues[i];
                    if (argb != 0)
                        ColorValues[i] = Color.FromArgb(argb);
                }
                SyncDxLogFormLayoutForPersistence();
            }
            catch { }
        }


        private void ApplyColoursFromCurrentFormLayout()
        {
            try
            {
                if (ColorValues == null || DefaultColors == null) return;

                int[] flags = FormLayout.ColorFlags;
                int count = Math.Min(ColorValues.Length, DefaultColors.Length);
                for (int i = 0; i < count; i++)
                {
                    // DXLog represents a reset/default entry as zero.  Convert that
                    // explicitly to the configured default instead of leaving the old
                    // custom colour in ColorValues.
                    int argb = flags != null && i < flags.Length ? flags[i] : 0;
                    ColorValues[i] = argb == 0 ? DefaultColors[i] : Color.FromArgb(argb);
                }

                SyncDxLogFormLayoutForPersistence();
            }
            catch { }
        }


        private void SchedulePersistentSave()
        {
            try
            {
                if (_persistSaveTimer == null)
                {
                    SavePersistentUiSettings();
                    return;
                }
                _persistSaveTimer.Stop();
                _persistSaveTimer.Start();
            }
            catch
            {
                try { SavePersistentUiSettings(); } catch { }
            }
        }

        private void StartStartupBoundsRestoreTimer()
        {
            try
            {
                if (_startupPositionRestoreDone) return;
                if (_startupBoundsRestoreTimer == null)
                {
                    ForceApplySavedLayout();
                    _startupPositionRestoreDone = true;
                    _allowPositionSave = true;
                    return;
                }
                _startupBoundsRestoreTimer.Stop();
                _startupBoundsRestoreTimer.Start();
            }
            catch
            {
                _startupPositionRestoreDone = true;
                _allowPositionSave = true;
            }
        }

        private void HookTitleBarMenuPersistence()
        {
            EnsureTitleBarContextMenuIsolation();
        }

        private void EnsureTitleBarContextMenuIsolation()
        {
            try
            {
                ContextMenuStrip current = ContextMenuStrip;
                if (current != null)
                {
                    _titleBarContextMenu = current;
                    if (!Object.ReferenceEquals(_hookedTitleBarContextMenu, current))
                    {
                        HookTitleBarMenuPersistence(current.Items);
                        current.Closed += delegate { SavePersistentUiSettings(); };
                        _hookedTitleBarContextMenu = current;
                    }
                }

                // Prevent the KForm colour/options menu being inherited by the
                // station list, messages, compose area and other child controls.
                if (ContextMenuStrip != null) ContextMenuStrip = null;
            }
            catch { }
        }

        private void InstallContextMenuShield(Control control, ref ContextMenuStrip shield)
        {
            if (control == null) return;
            try
            {
                shield = new ContextMenuStrip();
                shield.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs e)
                {
                    // A non-null child context menu prevents DXLog's parent KForm
                    // menu being inherited by the embedded ListView. The bridge's
                    // own IMessageFilter displays the callsign-specific menu.
                    e.Cancel = true;
                };
                control.ContextMenuStrip = shield;
            }
            catch { }
        }

        private void SuppressDxLogHostContextMenu()
        {
            Action closeHostMenu = delegate
            {
                try
                {
                    if (_titleBarContextMenu != null && _titleBarContextMenu.Visible)
                        _titleBarContextMenu.Close(ToolStripDropDownCloseReason.AppClicked);
                }
                catch { }
            };

            closeHostMenu();

            // DXLog can open its parent menu after the child ListView has processed
            // WM_RBUTTONUP. Close that delayed menu as well without touching the
            // bridge's newly created callsign menu.
            try
            {
                BeginInvoke(new Action(closeHostMenu));
                System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
                timer.Interval = 75;
                timer.Tick += delegate
                {
                    timer.Stop();
                    closeHostMenu();
                    timer.Dispose();
                };
                timer.Start();
            }
            catch { }
        }

        internal bool OwnsContextMenuHandle(IntPtr handle)
        {
            try
            {
                Control c = Control.FromHandle(handle);
                if (c == null) return false;
                if (Object.ReferenceEquals(c, this) || Contains(c)) return true;
                return _mapForm != null && !_mapForm.IsDisposed &&
                       (Object.ReferenceEquals(c, _mapForm) || _mapForm.Contains(c));
            }
            catch { return false; }
        }

        internal bool IsTitleBarContextPoint(Point screenPoint)
        {
            try
            {
                Point client = PointToClient(screenPoint);
                int stripHeight = Math.Max(22, SystemInformation.CaptionHeight);

                // A true non-client caption converts to a negative client Y.
                if (client.Y < 0 && client.Y >= -(stripHeight + 12)) return true;

                // DXLog KForm uses a slim custom title strip inside the window.
                return client.X >= 0 && client.X < Width && client.Y >= 0 && client.Y < stripHeight;
            }
            catch { return false; }
        }

        internal void ShowTitleBarContextMenu(Point screenPoint)
        {
            try
            {
                EnsureTitleBarContextMenuIsolation();
                if (_titleBarContextMenu == null) return;
                _titleBarContextMenu.Show(screenPoint);
            }
            catch { }
        }

        internal void HandleBridgeRightClick(IntPtr handle, Point screenPoint)
        {
            try
            {
                EnsureTitleBarContextMenuIsolation();

                Control source = Control.FromHandle(handle);
                if (source == null) return;

                if (_mapForm != null && !_mapForm.IsDisposed &&
                    (Object.ReferenceEquals(source, _mapForm) || _mapForm.Contains(source)))
                {
                    _mapForm.HandleBridgeRightClick(screenPoint);
                    return;
                }

                if (IsTitleBarContextPoint(screenPoint))
                {
                    ShowTitleBarContextMenu(screenPoint);
                    return;
                }

                SuppressDxLogHostContextMenu();

                if (IsSameOrChildControl(source, _users))
                {
                    ShowUserContextMenu(screenPoint);
                    return;
                }

                if (IsSameOrChildControl(source, _messages))
                {
                    ShowMessageContextMenu(_messages, screenPoint);
                    return;
                }

                if (IsSameOrChildControl(source, _threadMessages))
                {
                    ShowMessageContextMenu(_threadMessages, screenPoint);
                    return;
                }

                if (_macroButtons != null)
                {
                    for (int i = 0; i < _macroButtons.Length; i++)
                    {
                        if (IsSameOrChildControl(source, _macroButtons[i]))
                        {
                            EditMacrosClicked(i);
                            return;
                        }
                    }
                }

                if (IsSameOrChildControl(source, _statusLabel) || IsSameOrChildControl(source, _airScoutStatusLabel))
                {
                    ShowPerformanceDiagnostics();
                    return;
                }

                if (IsSameOrChildControl(source, _composeBox))
                {
                    ShowComposeContextMenu(screenPoint);
                    return;
                }

                // Blank bridge areas intentionally have no right-click menu.  The
                // message filter still consumes the mouse message so it cannot fall
                // through to DXLog's log-entry context menu.
            }
            catch { }
        }

        private static bool IsSameOrChildControl(Control source, Control parent)
        {
            if (source == null || parent == null) return false;
            Control current = source;
            while (current != null)
            {
                if (Object.ReferenceEquals(current, parent)) return true;
                current = current.Parent;
            }
            return false;
        }

        private void UsersMouseMove(object sender, MouseEventArgs e)
        {
            if (_users == null || _macroToolTip == null) return;
            try
            {
                ListViewHitTestInfo hit = _users.HitTest(e.Location);
                if (hit == null || hit.Item == null || hit.SubItem == null)
                {
                    if (_lastNameHoverCall.Length > 0)
                    {
                        _lastNameHoverCall = "";
                        _macroToolTip.Hide(_users);
                    }
                    return;
                }

                int subItemIndex = hit.Item.SubItems.IndexOf(hit.SubItem);
                if (subItemIndex != UserColName)
                {
                    if (_lastNameHoverCall.Length > 0)
                    {
                        _lastNameHoverCall = "";
                        _macroToolTip.Hide(_users);
                    }
                    return;
                }

                string call = CleanCall(hit.Item.Text);
                if (String.Equals(call, _lastNameHoverCall, StringComparison.OrdinalIgnoreCase)) return;
                _lastNameHoverCall = call;

                // Rebuild first so the popup contains the full decoded name plus
                // locator, activity, worked bands, notes and AirScout information.
                UpdateAirScoutItemToolTip(hit.Item);
                string tip = hit.Item.ToolTipText;
                if (String.IsNullOrWhiteSpace(tip)) tip = hit.SubItem.Text ?? "";
                _macroToolTip.Hide(_users);
                _macroToolTip.Show(tip, _users, e.X + 18, e.Y + 18, 12000);
            }
            catch { }
        }

        private void ShowUserContextMenu(Point screenPoint)
        {
            SuppressDxLogHostContextMenu();
            if (_users == null) return;
            Point client = _users.PointToClient(screenPoint);
            ListViewItem item = _users.GetItemAt(client.X, client.Y);
            if (item == null && _users.SelectedItems.Count > 0) item = _users.SelectedItems[0];
            if (item == null) return;
            item.Selected = true;
            item.Focused = true;
            ContextMenuStrip menu = MakeCallMenu(item.Text);
            menu.Show(_users, client);
        }

        private void ShowMessageContextMenu(ListView list, Point screenPoint)
        {
            SuppressDxLogHostContextMenu();
            if (list == null) return;
            Point client = list.PointToClient(screenPoint);
            ListViewItem item = list.GetItemAt(client.X, client.Y);
            if (item == null && list.SelectedItems.Count > 0) item = list.SelectedItems[0];
            if (item == null) return;
            item.Selected = true;
            item.Focused = true;

            string call = item.SubItems.Count > 1 ? item.SubItems[1].Text : "";
            KstParsedLine msg = item.Tag as KstParsedLine;
            string other = GetOtherPartyForMessage(msg);
            if (!String.IsNullOrWhiteSpace(other)) call = other;
            if (String.IsNullOrWhiteSpace(call)) return;

            ContextMenuStrip menu = MakeCallMenu(call);
            menu.Show(list, client);
        }

        private void ShowComposeContextMenu(Point screenPoint)
        {
            if (_composeBox == null) return;
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem cut = new ToolStripMenuItem("Cut");
            cut.Enabled = _composeBox.SelectionLength > 0;
            cut.Click += delegate { _composeBox.Cut(); };
            ToolStripMenuItem copy = new ToolStripMenuItem("Copy");
            copy.Enabled = _composeBox.SelectionLength > 0;
            copy.Click += delegate { _composeBox.Copy(); };
            ToolStripMenuItem paste = new ToolStripMenuItem("Paste");
            paste.Enabled = Clipboard.ContainsText();
            paste.Click += delegate { _composeBox.Paste(); };
            ToolStripMenuItem selectAll = new ToolStripMenuItem("Select all");
            selectAll.Enabled = _composeBox.TextLength > 0;
            selectAll.Click += delegate { _composeBox.SelectAll(); };
            menu.Items.Add(cut);
            menu.Items.Add(copy);
            menu.Items.Add(paste);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(selectAll);
            menu.Show(_composeBox, _composeBox.PointToClient(screenPoint));
        }

        private void HookTitleBarMenuPersistence(ToolStripItemCollection items)
        {
            if (items == null) return;
            foreach (ToolStripItem item in items)
            {
                ToolStripMenuItem mi = item as ToolStripMenuItem;
                if (mi == null) continue;
                if (!String.IsNullOrEmpty(mi.Name) && mi.Name.StartsWith("tbColor", StringComparison.OrdinalIgnoreCase))
                {
                    mi.Click += delegate
                    {
                        BeginInvoke(new MethodInvoker(delegate
                        {
                            // Run after KForm's own title colour click handler.
                            SavePersistentUiSettings();
                            try { Invalidate(true); } catch { }
                        }));
                    };
                }
                if (mi.DropDownItems != null && mi.DropDownItems.Count > 0)
                    HookTitleBarMenuPersistence(mi.DropDownItems);
            }
        }

        private void SyncDxLogFormLayoutForPersistence()
        {
            try
            {
                DALHeader.StructFormLayout fml = FormLayout;

                if (WindowState == FormWindowState.Normal && Width >= 100 && Height >= 100)
                {
                    fml.LocX = SafeShort(Location.X);
                    fml.LocY = SafeShort(Location.Y);
                    fml.Width = SafeShort(Width);
                    fml.Height = SafeShort(Height);
                }

                string tbColour = ReadCurrentTitleBarColourNumber();
                if (!String.IsNullOrEmpty(tbColour))
                {
                    fml.TitleBarColor = tbColour;
                    if (_settings != null) _settings.TitleBarColor = tbColour;
                }
                else if (_settings != null && !String.IsNullOrEmpty(_settings.TitleBarColor))
                    fml.TitleBarColor = _settings.TitleBarColor;

                if (ColorValues != null && ColorValues.Length > 0)
                {
                    if (fml.ColorFlags == null || fml.ColorFlags.Length < 20)
                        fml.ColorFlags = new int[20];

                    for (int i = 0; i < ColorValues.Length && i < fml.ColorFlags.Length; i++)
                    {
                        Color c = ColorValues[i];
                        fml.ColorFlags[i] = c.IsEmpty ? 0 : c.ToArgb();
                    }
                }

                FormLayout = fml;
            }
            catch { }
        }

        private static short SafeShort(int value)
        {
            if (value < short.MinValue) return short.MinValue;
            if (value > short.MaxValue) return short.MaxValue;
            return (short)value;
        }

        private void SavePersistentUiSettings()
        {
            try
            {
                if (_settings == null) return;

                if (_allowPositionSave)
                {
                    RememberCurrentWindowBounds();
                    SyncDxLogFormLayoutForPersistence();
                }

                string tbColour = ReadCurrentTitleBarColourNumber();
                if (!String.IsNullOrEmpty(tbColour))
                    _settings.TitleBarColor = tbColour;
                else if (!String.IsNullOrEmpty(FormLayout.TitleBarColor))
                    _settings.TitleBarColor = FormLayout.TitleBarColor;

                if (ColorValues != null && ColorValues.Length > 0)
                {
                    int[] values = new int[Math.Min(20, ColorValues.Length)];
                    for (int i = 0; i < values.Length; i++)
                    {
                        Color c = ColorValues[i];
                        values[i] = c.IsEmpty ? 0 : c.ToArgb();
                    }
                    _settings.ColorValues = values;
                }

                _settings.Save();
            }
            catch { }
        }

        private void RememberCurrentWindowBounds()
        {
            try
            {
                if (_settings == null) return;

                Rectangle b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                if (b.Width < 100 || b.Height < 100) return;

                _settings.WindowX = b.X;
                _settings.WindowY = b.Y;
                _settings.WindowW = b.Width;
                _settings.WindowH = b.Height;

                DALHeader.StructFormLayout fml = FormLayout;
                fml.LocX = SafeShort(b.X);
                fml.LocY = SafeShort(b.Y);
                fml.Width = SafeShort(b.Width);
                fml.Height = SafeShort(b.Height);
                string tbColour = ReadCurrentTitleBarColourNumber();
                if (!String.IsNullOrEmpty(tbColour))
                {
                    _settings.TitleBarColor = tbColour;
                    fml.TitleBarColor = tbColour;
                }
                else
                    fml.TitleBarColor = _settings.TitleBarColor ?? fml.TitleBarColor;
                FormLayout = fml;
            }
            catch { }
        }

        private void ApplySavedWindowBounds()
        {
            try
            {
                if (_settings == null || !_settings.HasWindowBounds) return;

                int w = Math.Max(MinimumSize.Width, _settings.WindowW);
                int h = Math.Max(MinimumSize.Height, _settings.WindowH);
                int x = _settings.WindowX;
                int y = _settings.WindowY;

                Rectangle wanted = new Rectangle(x, y, w, h);
                bool visibleOnScreen = IsMdiChild;
                if (!visibleOnScreen)
                {
                    foreach (Screen screen in Screen.AllScreens)
                    {
                        Rectangle area = screen.WorkingArea;
                        Rectangle test = Rectangle.Intersect(area, wanted);
                        if (test.Width >= 120 && test.Height >= 80)
                        {
                            visibleOnScreen = true;
                            break;
                        }
                    }
                }
                if (!visibleOnScreen) return;

                _restoringWindowBounds = true;
                try
                {
                    StartPosition = FormStartPosition.Manual;
                    Bounds = new Rectangle(x, y, w, h);

                    DALHeader.StructFormLayout fml = FormLayout;
                    fml.LocX = SafeShort(x);
                    fml.LocY = SafeShort(y);
                    fml.Width = SafeShort(w);
                    fml.Height = SafeShort(h);
                    if (!String.IsNullOrEmpty(_settings.TitleBarColor))
                        fml.TitleBarColor = _settings.TitleBarColor;
                    FormLayout = fml;
                }
                finally
                {
                    _restoringWindowBounds = false;
                }
            }
            catch { }
        }

        private void ApplySplitSize()
        {
            if (_split == null || _split.IsDisposed || _split.Width <= 0) return;

            try
            {
                int width = _split.Width;
                int splitter = _split.SplitterWidth;

                // Keep the user list wide when there is room, but never request a
                // SplitterDistance outside the valid WinForms range. This avoids
                // DXLog crashing while the form is still being created.
                int desired = UserListPanelWidth;
                int maxForLeft = Math.Max(50, width - 50 - splitter);
                desired = Math.Min(desired, maxForLeft);

                // Try to leave a sensible chat pane, but shrink gracefully on
                // smaller DXLog windows.
                int maxWithChatPane = width - 420 - splitter;
                if (maxWithChatPane > 300)
                    desired = Math.Min(desired, maxWithChatPane);

                desired = Math.Max(50, desired);

                int panel1Min = Math.Min(UserListPanelMinWidth, desired);
                int panel2Space = Math.Max(50, width - desired - splitter);
                int panel2Min = Math.Min(420, panel2Space);

                // Lower the min sizes first so changing SplitterDistance is always valid.
                _split.Panel1MinSize = 50;
                _split.Panel2MinSize = 50;

                if (desired > 0 && desired < width - splitter && Math.Abs(_split.SplitterDistance - desired) > 2)
                    _split.SplitterDistance = desired;

                _split.Panel1MinSize = panel1Min;
                _split.Panel2MinSize = panel2Min;
            }
            catch { }

            AdjustUserColumns();
            AdjustMessageColumns();
        }

        private void UsersColumnWidthChanging(object sender, ColumnWidthChangingEventArgs e)
        {
            if (_users == null || e.ColumnIndex < 0 || e.ColumnIndex >= _users.Columns.Count) return;
            if (_adjustingUserColumns) return;

            // The Name column may be adjusted, but never beyond the width that keeps
            // every station-list column inside the visible left pane.  This prevents
            // a saved or manually enlarged Name column from creating a horizontal bar.
            if (e.ColumnIndex == UserColName)
            {
                int available = Math.Max(180, _users.ClientSize.Width - 10);
                int otherWidth = 0;
                for (int i = 0; i < _users.Columns.Count; i++)
                {
                    if (i != UserColName) otherWidth += _users.Columns[i].Width;
                }

                int maximumNameWidth = Math.Max(20, available - otherWidth);
                e.NewWidth = Math.Max(20, Math.Min(Math.Min(420, maximumNameWidth), e.NewWidth));
                if (_settings != null)
                {
                    _settings.UserNameColumnWidth = e.NewWidth;
                    SchedulePersistentSave();
                }
                return;
            }

            e.Cancel = true;
            e.NewWidth = _users.Columns[e.ColumnIndex].Width;
        }

        private void AdjustUserColumns()
        {
            if (_users == null || _users.Columns.Count < UserColFirstWorkedBand) return;

            // The complete station row must always fit the visible left pane.  Start
            // with compact preferred widths and give all remaining space to Name.
            // When many worked-band columns are enabled, scale every column together
            // rather than allowing Windows to add a horizontal scrollbar.
            int available = Math.Max(180, _users.ClientSize.Width - 10);
            int bandCount = Math.Max(0, _users.Columns.Count - UserColFirstWorkedBand);

            int[] widths = new int[_users.Columns.Count];
            widths[UserColCall] = 82;
            widths[UserColName] = 64;
            widths[UserColLocator] = 64;
            widths[UserColQtf] = 46;
            widths[UserColQrb] = 58;
            widths[UserColAirScout] = 46;
            widths[UserColActive] = 56;
            widths[UserColUnread] = 38;
            for (int i = UserColFirstWorkedBand; i < widths.Length; i++) widths[i] = 36;

            int fixedWithoutName = 0;
            for (int i = 0; i < widths.Length; i++)
            {
                if (i != UserColName) fixedWithoutName += widths[i];
            }

            if (fixedWithoutName + widths[UserColName] <= available)
            {
                // Fill the pane exactly; no unused strip and no horizontal scroll.
                widths[UserColName] = Math.Max(64, available - fixedWithoutName);
            }
            else
            {
                int preferredTotal = fixedWithoutName + widths[UserColName];
                double scale = preferredTotal > 0 ? (double)available / preferredTotal : 1.0;
                int scaledTotal = 0;
                for (int i = 0; i < widths.Length; i++)
                {
                    widths[i] = Math.Max(20, (int)Math.Floor(widths[i] * scale));
                    scaledTotal += widths[i];
                }

                // If the hard minimums pushed the result a few pixels over, remove
                // them from the widest columns until the exact available width fits.
                while (scaledTotal > available)
                {
                    int widest = -1;
                    for (int i = 0; i < widths.Length; i++)
                    {
                        if (widths[i] > 20 && (widest < 0 || widths[i] > widths[widest])) widest = i;
                    }
                    if (widest < 0) break;
                    widths[widest]--;
                    scaledTotal--;
                }

                if (scaledTotal < available)
                    widths[UserColName] += available - scaledTotal;
            }

            _adjustingUserColumns = true;
            try
            {
                for (int i = 0; i < widths.Length && i < _users.Columns.Count; i++)
                {
                    if (_users.Columns[i].Width != widths[i]) _users.Columns[i].Width = widths[i];
                }
            }
            finally
            {
                _adjustingUserColumns = false;
            }
        }

        private List<KstWorkedBandOption> GetVisibleWorkedBandOptions()
        {
            return KstWorkedBands.GetSelectedOptions(_settings == null ? null : _settings.WorkedBandColumns);
        }

        private void RebuildUserColumns()
        {
            if (_users == null) return;
            _users.BeginUpdate();
            try
            {
                _users.Columns.Clear();
                _users.Columns.Add("Call", 86);
                _users.Columns.Add("Name", 165);
                _users.Columns.Add("QRA", 72);
                _users.Columns.Add("QTF", 52);
                _users.Columns.Add("QRB", 72);
                _users.Columns.Add("AS", 54);
                _users.Columns.Add("Active", 64);
                _users.Columns.Add("Msg", 42);
                foreach (KstWorkedBandOption band in GetVisibleWorkedBandOptions())
                    _users.Columns.Add(band.Header, 44);
                if (_userSortColumn >= _users.Columns.Count) _userSortColumn = UserColCall;
            }
            finally
            {
                _users.EndUpdate();
            }
            AdjustUserColumns();
            UpdateUserColumnHeaders();
        }

        private void EnsureUserSubItems(ListViewItem item)
        {
            if (item == null || _users == null) return;
            while (item.SubItems.Count < _users.Columns.Count) item.SubItems.Add("");
            while (item.SubItems.Count > _users.Columns.Count) item.SubItems.RemoveAt(item.SubItems.Count - 1);
        }

        private string GetWorkedBandCell(string call, string bandKey)
        {
            HashSet<string> bands;
            if (!_workedBandsByCall.TryGetValue(CleanCall(call), out bands) || bands == null) return "";
            return bands.Contains(KstWorkedBands.NormalizeKey(bandKey)) ? "1" : "";
        }

        private void AdjustMessageColumns()
        {
            AdjustMessageColumns(_messages);
            AdjustMessageColumns(_threadMessages);
        }

        private void AdjustMessageColumns(ListView list)
        {
            if (list == null || list.Columns.Count < 4) return;

            // ClientSize already excludes a native vertical scrollbar when one is
            // present.  The previous extra 28 px allowance therefore left a large
            // unpainted section at the right of the owner-drawn header, which
            // Windows displayed as a white square.  Fill the available header
            // width and retain only a two-pixel safety margin for the border.
            const int utcWidth = 70;
            const int fromWidth = 90;
            const int nameWidth = 120;
            const int edgeMargin = 2;

            int msgWidth = Math.Max(220,
                list.ClientSize.Width - utcWidth - fromWidth - nameWidth - edgeMargin);

            list.Columns[0].Width = utcWidth;
            list.Columns[1].Width = fromWidth;
            list.Columns[2].Width = nameWidth;
            list.Columns[3].Width = msgWidth;
        }

        private Color KstColor(string name, Color fallback)
        {
            try
            {
                if (ColorSetTypes == null || ColorValues == null) return fallback;
                int index = Array.IndexOf(ColorSetTypes, name);
                if (index < 0 || index >= ColorValues.Length) return fallback;
                Color c = ColorValues[index];
                if (c.A != 255) return fallback;
                return c;
            }
            catch { return fallback; }
        }

        private void ApplyWindowColors()
        {
            Color windowBack = KstColor("Window background", SystemColors.Control);
            Color windowFore = KstColor("Window text", SystemColors.ControlText);
            BackColor = windowBack;
            ForeColor = windowFore;
            if (_layout != null) { _layout.BackColor = windowBack; _layout.ForeColor = windowFore; }
            if (_split != null) { _split.BackColor = windowBack; _split.ForeColor = windowFore; }
            if (_statusLabel != null) { _statusLabel.BackColor = windowBack; _statusLabel.ForeColor = windowFore; }
            if (_airScoutStatusLabel != null) { _airScoutStatusLabel.BackColor = windowBack; _airScoutStatusLabel.ForeColor = windowFore; }
            if (_roomCombo != null)
            {
                _roomCombo.BackColor = KstColor("List background", SystemColors.Window);
                _roomCombo.ForeColor = KstColor("List text", SystemColors.WindowText);
            }
            if (_distanceCombo != null)
            {
                _distanceCombo.BackColor = KstColor("List background", SystemColors.Window);
                _distanceCombo.ForeColor = KstColor("List text", SystemColors.WindowText);
            }
            if (_composeBox != null)
            {
                _composeBox.BackColor = KstColor("List background", SystemColors.Window);
                _composeBox.ForeColor = KstColor("List text", SystemColors.WindowText);
            }
            if (_users != null)
            {
                _users.BackColor = KstColor("List background", SystemColors.Window);
                _users.ForeColor = KstColor("List text", SystemColors.WindowText);
            }
            if (_messages != null)
            {
                _messages.BackColor = KstColor("List background", SystemColors.Window);
                _messages.ForeColor = KstColor("List text", SystemColors.WindowText);
            }
            if (_threadMessages != null)
            {
                _threadMessages.BackColor = KstColor("List background", SystemColors.Window);
                _threadMessages.ForeColor = KstColor("List text", SystemColors.WindowText);
            }
            if (_threadHeaderLabel != null)
            {
                _threadHeaderLabel.BackColor = windowBack;
                _threadHeaderLabel.ForeColor = windowFore;
            }
            if (_composeTargetLabel != null)
            {
                _composeTargetLabel.BackColor = KstColor("List background", SystemColors.Window);
                _composeTargetLabel.ForeColor = KstColor("List text", SystemColors.WindowText);
            }

            ApplyButtonColors();
        }

        private void ApplyButtonColors()
        {
            Color buttonBack = KstColor("Button background", SystemColors.Control);
            Color buttonFore = KstColor("Button text", SystemColors.ControlText);

            ApplyButtonColors(_setupButton, buttonBack, buttonFore);
            ApplyButtonColors(_mapButton, buttonBack, buttonFore);
            ApplyButtonColors(_connectButton, buttonBack, buttonFore);
            ApplyButtonColors(_disconnectButton, buttonBack, buttonFore);
            ApplyButtonColors(_sendButton, buttonBack, buttonFore);
            ApplyButtonColors(_cqButton, buttonBack, buttonFore);

            if (_macroButtons != null)
            {
                foreach (Button b in _macroButtons)
                    ApplyButtonColors(b, buttonBack, buttonFore);
            }
        }

        private static void ApplyButtonColors(Button button, Color back, Color fore)
        {
            if (button == null) return;
            button.BackColor = back;
            button.ForeColor = fore;
            button.UseVisualStyleBackColor = false;
        }

        private static void ApplyItemStyleToSubItems(ListViewItem item, Color back, Color fore, Font font)
        {
            if (item == null) return;
            item.UseItemStyleForSubItems = false;
            item.BackColor = back;
            item.ForeColor = fore;
            item.Font = font;
            foreach (ListViewItem.ListViewSubItem sub in item.SubItems)
            {
                sub.BackColor = back;
                sub.ForeColor = fore;
                sub.Font = font;
            }
        }

        private void RestyleUsers()
        {
            if (_users == null) return;
            foreach (ListViewItem item in _users.Items)
            {
                KstUserInfo user = item.Tag as KstUserInfo;
                StyleUserItem(item, user);
            }
        }

        private void RestyleMessages()
        {
            if (_messages == null) return;
            foreach (ListViewItem item in _messages.Items)
            {
                KstParsedLine msg = item.Tag as KstParsedLine;
                bool worked = msg != null && msg.Worked;
                StyleMessageItem(item, msg, worked);
            }
        }

        private void RestyleThreadMessages()
        {
            if (_threadMessages == null) return;
            foreach (ListViewItem item in _threadMessages.Items)
            {
                KstParsedLine msg = item.Tag as KstParsedLine;
                bool worked = msg != null && msg.Worked;
                StyleMessageItem(item, msg, worked);
            }
        }

        private void MessagesSelectedIndexChanged()
        {
            ListViewItem current = _messages != null && _messages.SelectedItems.Count > 0 ? _messages.SelectedItems[0] : null;
            ListViewItem previous = _lastStyledMessageItem;
            _lastStyledMessageItem = current;
            RestyleMessageItem(previous);
            RestyleMessageItem(current);
            UpdateLastSelectedCallFromMessageList(_messages);
            MarkCallRead(_lastSelectedCall);
            RefreshConversationView();
            UpdateComposeTarget();
        }

        private void ThreadMessagesSelectedIndexChanged()
        {
            ListViewItem current = _threadMessages != null && _threadMessages.SelectedItems.Count > 0 ? _threadMessages.SelectedItems[0] : null;
            ListViewItem previous = _lastStyledThreadItem;
            _lastStyledThreadItem = current;
            RestyleMessageItem(previous);
            RestyleMessageItem(current);
            UpdateLastSelectedCallFromMessageList(_threadMessages);
            MarkCallRead(_lastSelectedCall);
            UpdateComposeTarget();
        }

        private void RestyleMessageItem(ListViewItem item)
        {
            if (item == null || item.ListView == null) return;
            KstParsedLine msg = item.Tag as KstParsedLine;
            StyleMessageItem(item, msg, msg != null && msg.Worked);
            try { item.ListView.Invalidate(item.Bounds); } catch { }
        }

        private void StyleUserItem(ListViewItem item, KstUserInfo user)
        {
            if (item == null) return;
            if (item.Selected)
            {
                ApplyItemStyleToSubItems(item,
                    KstColor("Selected row background", Color.Yellow),
                    KstColor("Selected row text", Color.Black),
                    _boldFont ?? _windowFont);
            }
            else
            {
                // Worked status is shown explicitly by green ticks in the selected
                // band columns. Keep the rest of the row in the normal DXLog list
                // style instead of greying or italicising the whole station.
                ApplyItemStyleToSubItems(item,
                    KstColor("List background", SystemColors.Window),
                    KstColor("List text", SystemColors.WindowText),
                    _windowFont);
            }
        }

        private void StyleMessageItem(ListViewItem item, KstParsedLine msg, bool worked)
        {
            if (item == null) return;
            bool selected = item.Selected;
            bool directToMe = IsDirectToMe(msg);

            if (selected)
            {
                ApplyItemStyleToSubItems(item,
                    KstColor("Selected row background", Color.Yellow),
                    KstColor("Selected row text", Color.Black),
                    _boldFont ?? _windowFont);
            }
            else if (directToMe)
            {
                // Only highlight directed ON4KST messages when the target call in
                // brackets matches the logged-in callsign from Setup / User call.
                // Example: with User/call M0CKE, only "(M0CKE) ..." is highlighted.
                ApplyItemStyleToSubItems(item,
                    KstColor("Direct message background", Color.FromArgb(255, 220, 120)),
                    KstColor("Direct message text", Color.Black),
                    _boldFont ?? _windowFont);
            }
            else if (worked)
            {
                ApplyItemStyleToSubItems(item,
                    KstColor("In log background", KstColor("Worked/B4 background", Color.Gainsboro)),
                    KstColor("In log text", KstColor("Worked/B4 text", Color.DimGray)),
                    _italicFont ?? _windowFont);
            }
            else if (msg != null && msg.Type == KstParsedType.Prompt)
            {
                ApplyItemStyleToSubItems(item,
                    KstColor("System message background", Color.FromArgb(220, 240, 255)),
                    KstColor("System message text", Color.Navy),
                    _windowFont);
            }
            else
            {
                ApplyItemStyleToSubItems(item,
                    KstColor("List background", SystemColors.Window),
                    KstColor("List text", SystemColors.WindowText),
                    _windowFont);
            }
        }

        private bool IsDirectToMe(KstParsedLine msg)
        {
            if (msg == null || String.IsNullOrWhiteSpace(msg.Message) || _settings == null) return false;

            string myCall = CleanCall(_settings.Callsign);
            if (String.IsNullOrWhiteSpace(myCall)) return false;

            // ON4KST directed messages are shown in the chat text as:
            //   (CALL) message text
            // Only highlight when the bracketed target is exactly the callsign from
            // Setup / User call, e.g. User/call M0CKE highlights only "(M0CKE) ...".
            // Do not highlight "(OTHER) ..." messages between other stations.
            string directedTarget = GetDirectedTarget(msg.Message);
            return String.Equals(directedTarget, myCall, StringComparison.OrdinalIgnoreCase);
        }

        private string GetDirectedTarget(string message)
        {
            if (String.IsNullOrWhiteSpace(message)) return "";

            // Be tolerant of spaces and suffixes inside the brackets.
            Match m = Regex.Match(message, @"^\s*\(([^\)]+)\)", RegexOptions.IgnoreCase);
            return m.Success ? CleanCall(m.Groups[1].Value) : "";
        }

        private void DrawListColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            Color back = KstColor("Window background", SystemColors.Control);
            Color fore = KstColor("Window text", SystemColors.ControlText);
            using (SolidBrush brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, _boldFont ?? _windowFont, e.Bounds, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private void DrawListSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            Color back = e.SubItem.BackColor.IsEmpty ? e.Item.BackColor : e.SubItem.BackColor;
            Color fore = e.SubItem.ForeColor.IsEmpty ? e.Item.ForeColor : e.SubItem.ForeColor;
            if (back.A != 255) back = KstColor("List background", SystemColors.Window);
            if (fore.A != 255) fore = KstColor("List text", SystemColors.WindowText);

            using (SolidBrush brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, e.Bounds);

            if (ReferenceEquals(sender, _users) && e.ColumnIndex == UserColCall && IsWatchedCall(e.Item.Text))
            {
                DrawWatchedCallCell(e.Graphics, e.Bounds, e.SubItem.Text, fore, e.SubItem.Font ?? e.Item.Font ?? _windowFont);
                return;
            }
            if (ReferenceEquals(sender, _users) && e.ColumnIndex == UserColAirScout)
            {
                DrawAirScoutOpportunityCell(e.Graphics, e.Bounds, e.SubItem.Text, fore);
                return;
            }
            if (ReferenceEquals(sender, _users) && e.ColumnIndex == UserColUnread)
            {
                DrawUnreadCell(e.Graphics, e.Bounds, e.SubItem.Text);
                return;
            }
            if (ReferenceEquals(sender, _users) && e.ColumnIndex >= UserColFirstWorkedBand)
            {
                if (!String.IsNullOrWhiteSpace(e.SubItem.Text)) DrawGreenWorkedTick(e.Graphics, e.Bounds);
                return;
            }

            Font f = e.SubItem.Font ?? e.Item.Font ?? _windowFont;
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, f, e.Bounds, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private static void DrawWatchedCallCell(Graphics g, Rectangle bounds, string value, Color textColour, Font font)
        {
            Rectangle starBounds = new Rectangle(bounds.Left + 2, bounds.Top, 16, bounds.Height);
            TextRenderer.DrawText(g, "★", font, starBounds, Color.Goldenrod,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            Rectangle callBounds = new Rectangle(bounds.Left + 17, bounds.Top, Math.Max(1, bounds.Width - 17), bounds.Height);
            TextRenderer.DrawText(g, value ?? "", font, callBounds, textColour,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void DrawUnreadCell(Graphics g, Rectangle bounds, string value)
        {
            int count;
            if (!Int32.TryParse(value, out count) || count <= 0) return;
            Rectangle bubble = new Rectangle(bounds.Left + 5, bounds.Top + Math.Max(2, (bounds.Height - 18) / 2), Math.Max(24, bounds.Width - 10), 18);
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(235, 126, 34))) g.FillEllipse(fill, bubble);
            TextRenderer.DrawText(g, count > 99 ? "99+" : count.ToString(), _boldFont ?? _windowFont, bubble, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void DrawAirScoutOpportunityCell(Graphics g, Rectangle bounds, string value, Color textColour)
        {
            value = (value ?? "").Trim();
            if (value.Length == 0) return;
            if (value == "-")
            {
                TextRenderer.DrawText(g, value, _windowFont, bounds, textColour,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }

            bool now = String.Equals(value, "NOW", StringComparison.OrdinalIgnoreCase);
            Color blob = now ? Color.LimeGreen : Color.Orange;
            int diameter = 11;
            int x = bounds.Left + 7;
            int y = bounds.Top + Math.Max(1, (bounds.Height - diameter) / 2);
            SmoothingMode oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(blob)) g.FillEllipse(b, x, y, diameter, diameter);
            using (Pen p = new Pen(Color.FromArgb(150, Color.Black), 1f)) g.DrawEllipse(p, x, y, diameter, diameter);
            g.SmoothingMode = oldMode;

            if (!now)
            {
                Rectangle textBounds = new Rectangle(x + diameter + 4, bounds.Top, Math.Max(1, bounds.Right - (x + diameter + 4)), bounds.Height);
                TextRenderer.DrawText(g, value, _windowFont, textBounds, textColour,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        private static void DrawGreenWorkedTick(Graphics g, Rectangle bounds)
        {
            float cx = bounds.Left + bounds.Width / 2f;
            float cy = bounds.Top + bounds.Height / 2f;
            SmoothingMode oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            PointF[] points = new PointF[]
            {
                new PointF(cx - 7f, cy),
                new PointF(cx - 2f, cy + 5f),
                new PointF(cx + 8f, cy - 6f)
            };
            using (Pen outline = new Pen(Color.FromArgb(170, Color.Black), 4.6f))
            {
                outline.StartCap = LineCap.Round;
                outline.EndCap = LineCap.Round;
                g.DrawLines(outline, points);
            }
            using (Pen pen = new Pen(Color.LimeGreen, 2.6f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLines(pen, points);
            }
            g.SmoothingMode = oldMode;
        }

        private void AddLabel(string text, int column, int row)
        {
            _layout.Controls.Add(new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                AutoSize = false
            }, column, row);
        }

        private void HookEditableFocus(Control c)
        {
            if (c == null) return;
            c.MouseDown += delegate { CaptureInputFocus(c); };
            c.Enter += delegate { CaptureInputFocus(c); };
            c.GotFocus += delegate { CaptureInputFocus(c); };
        }

        private void CaptureInputFocus(Control c)
        {
            if (c == null || c.IsDisposed || !c.Enabled) return;
            _composeInputLocked = true;
            _inputFocusTarget = c;
            try
            {
                // DXLog installs its own keyboard filters. Re-add ours whenever the
                // operator enters the compose field so this filter is the newest and
                // gets first opportunity to redirect the keystroke.
                if (_composeInputFilter != null)
                {
                    Application.RemoveMessageFilter(_composeInputFilter);
                    Application.AddMessageFilter(_composeInputFilter);
                }
            }
            catch { }
            try
            {
                Activate();
                ActiveControl = c;
                c.Select();
                TextBox tb = c as TextBox;
                if (tb != null) tb.SelectionStart = tb.Text.Length;
            }
            catch { }
            if (_inputFocusTimer != null && !_inputFocusTimer.Enabled) _inputFocusTimer.Start();
        }

        private bool ShouldCaptureComposeInput()
        {
            return _composeInputLocked && _composeBox != null && !_composeBox.IsDisposed && _composeBox.Enabled && _composeBox.Visible && Visible;
        }

        private IntPtr GetComposeInputHandle()
        {
            try
            {
                if (_composeBox != null && !_composeBox.IsDisposed && _composeBox.IsHandleCreated) return _composeBox.Handle;
            }
            catch { }
            return IntPtr.Zero;
        }

        private void ReleaseComposeInputCapture()
        {
            _composeInputLocked = false;
            _inputFocusTarget = null;
            try { if (_inputFocusTimer != null) _inputFocusTimer.Stop(); } catch { }
        }

        private void HookReliableDropDown(ComboBox combo)
        {
            if (combo == null) return;

            combo.MouseDown += delegate
            {
                try
                {
                    if (combo.Enabled && !combo.DroppedDown)
                        BeginInvoke(new MethodInvoker(delegate
                        {
                            if (combo != null && !combo.IsDisposed && combo.Enabled)
                                combo.DroppedDown = true;
                        }));
                }
                catch { }
            };

            combo.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F4 || (e.Alt && e.KeyCode == Keys.Down))
                {
                    try { combo.DroppedDown = true; } catch { }
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
        }

        private void PopulateDistanceCombo(ComboBox combo, int selectedLimitKm)
        {
            if (combo == null) return;
            _applyingDistanceSelection = true;
            try
            {
                combo.BeginUpdate();
                combo.Items.Clear();
                int[] limits = new int[] { 0, 100, 200, 300, 400, 500, 1000, 1500, 2000 };
                int selectedIndex = 0;
                for (int i = 0; i < limits.Length; i++)
                {
                    KstDistanceOption option = new KstDistanceOption(limits[i]);
                    combo.Items.Add(option);
                    if (limits[i] == selectedLimitKm) selectedIndex = i;
                }
                combo.SelectedIndex = selectedIndex;
            }
            finally
            {
                combo.EndUpdate();
                _applyingDistanceSelection = false;
            }
        }

        private void ApplyDistanceFilterSelection()
        {
            KstDistanceOption option = _distanceCombo != null ? _distanceCombo.SelectedItem as KstDistanceOption : null;
            if (option == null || _settings == null) return;
            if (_settings.DistanceFilterKm == option.MaxKm) return;

            _settings.DistanceFilterKm = option.MaxKm;
            _settings.Save();
            RebuildVisibleUserList();
            ResetAirScoutAutoScan(true);
            UpdateStatus(option.MaxKm <= 0
                ? "Distance filter: All stations"
                : "Distance filter: 0-" + option.MaxKm.ToString() + " km");
        }

        private void PopulateAirScoutFilterCombo(ComboBox combo, int selectedMinutes)
        {
            if (combo == null) return;
            _applyingAirScoutFilterSelection = true;
            try
            {
                combo.BeginUpdate();
                combo.Items.Clear();
                int[] values = new int[] { -1, 0, 5, 10, 20 };
                int selectedIndex = 0;
                for (int i = 0; i < values.Length; i++)
                {
                    KstAirScoutFilterOption option = new KstAirScoutFilterOption(values[i]);
                    combo.Items.Add(option);
                    if (values[i] == selectedMinutes) selectedIndex = i;
                }
                combo.SelectedIndex = selectedIndex;
            }
            finally
            {
                combo.EndUpdate();
                _applyingAirScoutFilterSelection = false;
            }
        }

        private void ApplyAirScoutFilterSelection()
        {
            KstAirScoutFilterOption option = _airScoutFilterCombo != null ? _airScoutFilterCombo.SelectedItem as KstAirScoutFilterOption : null;
            if (option == null || _settings == null) return;
            if (_settings.AirScoutFilterMinutes == option.MaxMinutes) return;
            _settings.AirScoutFilterMinutes = option.MaxMinutes;
            _settings.Save();
            RebuildVisibleUserList();
            UpdateStatus("AirScout filter: " + option.ToString());
        }

        private void PopulateAircraftFilterCombo(ComboBox combo, int selectedMode)
        {
            if (combo == null) return;
            _applyingAircraftFilterSelection = true;
            try
            {
                combo.BeginUpdate();
                combo.Items.Clear();
                foreach (KstAircraftFilterOption option in KstAircraftFilterOption.GetOptions())
                {
                    combo.Items.Add(option);
                    if ((int)option.Mode == selectedMode) combo.SelectedItem = option;
                }
                if (combo.SelectedIndex < 0) combo.SelectedIndex = 0;
            }
            finally
            {
                combo.EndUpdate();
                _applyingAircraftFilterSelection = false;
            }
        }

        private void ApplyAircraftFilterSelection()
        {
            KstAircraftFilterOption option = _aircraftFilterCombo != null ? _aircraftFilterCombo.SelectedItem as KstAircraftFilterOption : null;
            if (option == null || _settings == null) return;
            int newMode = (int)option.Mode;
            if (_settings.AircraftFilterMode == newMode) return;

            _settings.AircraftFilterMode = newMode;
            _settings.Save();
            BeginRefreshAirScoutLivePlanes(true);
            RefreshAllAirScoutCells();
            RebuildVisibleUserList();
            RefreshMapAircraftOnly();
            UpdateStatus("Aircraft filter: " + option.ToString());
        }

        private void LoadWatchedCallsFromSettings()
        {
            _watchedCalls.Clear();
            if (_settings == null || _settings.WatchedCalls == null) return;
            foreach (string call in _settings.WatchedCalls)
            {
                string clean = CleanCall(call);
                if (!String.IsNullOrWhiteSpace(clean)) _watchedCalls.Add(clean);
            }
        }

        private bool IsWatchedCall(string call)
        {
            return _watchedCalls.Contains(CleanCall(call));
        }

        private void ToggleWatchCall(string call)
        {
            call = CleanCall(call);
            if (String.IsNullOrWhiteSpace(call) || _settings == null) return;
            bool watched;
            if (_watchedCalls.Contains(call))
            {
                _watchedCalls.Remove(call);
                watched = false;
            }
            else
            {
                _watchedCalls.Add(call);
                watched = true;
            }
            List<string> saved = new List<string>(_watchedCalls);
            saved.Sort(StringComparer.OrdinalIgnoreCase);
            _settings.WatchedCalls = saved.ToArray();
            _settings.Save();
            UpdateUserInfoCells(call);
            RebuildVisibleUserList();
            RequestAirScoutRescan(true);
            UpdateStatus((watched ? "Watching " : "Stopped watching ") + call);
        }

        private bool IsUserWithinDistanceFilter(KstUserInfo user)
        {
            if (user == null) return false;
            if (IsWatchedCall(user.Call)) return true;
            int maxKm = _settings != null ? _settings.DistanceFilterKm : 0;
            if (maxKm <= 0) return true;

            string myLocator = NormalizeLocator(GetOwnLocator());
            string dxLocator = NormalizeLocator(user.Locator);
            if (!IsValidLocator(myLocator) || !IsValidLocator(dxLocator)) return false;

            try
            {
                double km = DistanceKm(LocatorToPoint(myLocator), LocatorToPoint(dxLocator));
                return km <= maxKm + 0.0001;
            }
            catch { return false; }
        }

        private AirScoutPlaneInfo GetBestFilteredAirScoutPlane(AirScoutPathResult result)
        {
            if (result == null || result.Planes == null) return null;
            AirScoutPlaneInfo best = null;
            foreach (AirScoutPlaneInfo plane in result.Planes)
            {
                if (plane == null || plane.Mins >= 30 || !MatchesAircraftFilter(plane)) continue;
                if (best == null || plane.Potential > best.Potential ||
                    (plane.Potential == best.Potential && plane.IntQRB < best.IntQRB))
                    best = plane;
            }
            return best;
        }

        private bool MatchesAircraftFilter(AirScoutPlaneInfo candidate)
        {
            if (candidate == null) return false;
            KstAircraftFilterMode mode = _settings == null
                ? KstAircraftFilterMode.All
                : KstAircraftFilterOption.NormalizeMode(_settings.AircraftFilterMode);
            if (mode == KstAircraftFilterMode.All) return true;

            int sizeRank = GetAirScoutSizeRank(candidate.Category);
            if (mode == KstAircraftFilterMode.XXAndXXX) return sizeRank >= 2;
            if (mode == KstAircraftFilterMode.XXXOnly) return sizeRank >= 3;

            AirScoutLivePlane live;
            TryGetLivePlaneForAirScoutCandidate(candidate.Call, out live);
            KstAircraftTrafficClass trafficClass = ClassifyAircraftTraffic(live, candidate);
            if (mode == KstAircraftFilterMode.PassengerOrCargo)
                return sizeRank >= 2 && (trafficClass == KstAircraftTrafficClass.Passenger || trafficClass == KstAircraftTrafficClass.Cargo);
            if (mode == KstAircraftFilterMode.CargoOnly)
                return trafficClass == KstAircraftTrafficClass.Cargo;
            return true;
        }

        private static int GetAirScoutSizeRank(string category)
        {
            string text = (category ?? "").Trim().ToUpperInvariant();
            if (text.Length == 0) return 0;
            if (text.IndexOf("XXX", StringComparison.Ordinal) >= 0) return 3;
            if (text.IndexOf("XX", StringComparison.Ordinal) >= 0) return 2;
            if (text.IndexOf("X", StringComparison.Ordinal) >= 0) return 1;
            if (text.IndexOf("HEAVY", StringComparison.Ordinal) >= 0 || text == "H") return 3;
            if (text.IndexOf("LARGE", StringComparison.Ordinal) >= 0 || text == "L") return 2;
            int numeric;
            if (Int32.TryParse(text, out numeric)) return Math.Max(0, Math.Min(3, numeric));
            return 0;
        }

        private static string GetAirScoutSizeText(AirScoutPlaneInfo candidate)
        {
            if (candidate == null) return "";
            string category = (candidate.Category ?? "").Trim();
            int rank = GetAirScoutSizeRank(category);
            if (rank == 3) return "XXX";
            if (rank == 2) return "XX";
            if (rank == 1) return "X";
            return category;
        }

        private static KstAircraftTrafficClass ClassifyAircraftTraffic(AirScoutLivePlane live, AirScoutPlaneInfo candidate)
        {
            string type = NormalizeAircraftId(live == null ? "" : live.Type);
            string flight = NormalizeAircraftId(live == null ? "" : live.Flight);
            string call = NormalizeAircraftId(live == null ? (candidate == null ? "" : candidate.Call) : live.Call);
            string combined = flight + " " + call;

            if (IsKnownCargoAircraftType(type) || IsKnownCargoCallsign(combined))
                return KstAircraftTrafficClass.Cargo;
            if (IsKnownPassengerAircraftType(type))
                return KstAircraftTrafficClass.Passenger;

            // A normal airline ICAO callsign is usually three letters followed by
            // digits. This is deliberately a fallback only; known cargo prefixes
            // and freighter type variants above take priority.
            if (Regex.IsMatch(flight, @"^[A-Z]{3}[0-9]") || Regex.IsMatch(call, @"^[A-Z]{3}[0-9]"))
                return KstAircraftTrafficClass.Passenger;
            return KstAircraftTrafficClass.Unknown;
        }

        private static bool IsKnownCargoCallsign(string value)
        {
            string text = NormalizeAircraftId(value);
            if (text.Length < 3) return false;
            string[] prefixes = new string[]
            {
                "FDX", "UPS", "BCS", "DHK", "DHA", "CLX", "ICL", "BOX", "GTI",
                "ABW", "CKS", "NCA", "PAC", "MPH", "MNB", "SRR", "TAY", "ABR",
                "ATG", "AJT", "CLU", "CGF", "CMB", "ACP", "KALCARGO"
            };
            foreach (string prefix in prefixes)
                if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool IsKnownCargoAircraftType(string type)
        {
            string t = NormalizeAircraftId(type);
            if (t.Length == 0) return false;
            string[] exact = new string[]
            {
                "B74F", "B744F", "B748F", "B77F", "B763F", "B762F", "B752F",
                "B738F", "B734F", "B733F", "B732F", "B722F", "A306F", "A30BF",
                "A332F", "A333F", "MD11F", "DC10F", "AT72F", "AT76F", "IL76",
                "AN12", "AN26", "AN72", "AN124", "AN225"
            };
            foreach (string code in exact)
                if (String.Equals(t, code, StringComparison.OrdinalIgnoreCase)) return true;
            if (t.EndsWith("F", StringComparison.OrdinalIgnoreCase) &&
                (t.StartsWith("B", StringComparison.OrdinalIgnoreCase) ||
                 t.StartsWith("A", StringComparison.OrdinalIgnoreCase) ||
                 t.StartsWith("MD", StringComparison.OrdinalIgnoreCase) ||
                 t.StartsWith("DC", StringComparison.OrdinalIgnoreCase) ||
                 t.StartsWith("AT", StringComparison.OrdinalIgnoreCase)))
                return true;
            return false;
        }

        private static bool IsKnownPassengerAircraftType(string type)
        {
            string t = NormalizeAircraftId(type);
            if (t.Length == 0 || IsKnownCargoAircraftType(t)) return false;
            return Regex.IsMatch(t, @"^(A2|A3|B7|B3|E1|E2|CRJ|DH8|AT4|AT7|F70|F100|MD8|MD9|BCS)");
        }

        private static string GetAircraftTrafficClassText(KstAircraftTrafficClass trafficClass)
        {
            if (trafficClass == KstAircraftTrafficClass.Cargo) return "Cargo";
            if (trafficClass == KstAircraftTrafficClass.Passenger) return "Passenger";
            return "Unknown";
        }

        private static string GetAircraftModelName(string type)
        {
            string t = NormalizeAircraftId(type);
            switch (t)
            {
                case "A388": return "Airbus A380-800";
                case "A359": return "Airbus A350-900";
                case "A35K": return "Airbus A350-1000";
                case "A339": return "Airbus A330-900neo";
                case "A333": return "Airbus A330-300";
                case "A332": return "Airbus A330-200";
                case "A321": return "Airbus A321";
                case "A21N": return "Airbus A321neo";
                case "A320": return "Airbus A320";
                case "A20N": return "Airbus A320neo";
                case "A319": return "Airbus A319";
                case "B748": return "Boeing 747-8";
                case "B744": return "Boeing 747-400";
                case "B77W": return "Boeing 777-300ER";
                case "B773": return "Boeing 777-300";
                case "B772": return "Boeing 777-200";
                case "B77L": return "Boeing 777-200LR";
                case "B77F": return "Boeing 777 Freighter";
                case "B78X": return "Boeing 787-10";
                case "B789": return "Boeing 787-9";
                case "B788": return "Boeing 787-8";
                case "B763": return "Boeing 767-300";
                case "B752": return "Boeing 757-200";
                case "B739": return "Boeing 737-900";
                case "B738": return "Boeing 737-800";
                case "B38M": return "Boeing 737 MAX 8";
                case "B39M": return "Boeing 737 MAX 9";
                case "MD11": return "McDonnell Douglas MD-11";
                case "MD11F": return "McDonnell Douglas MD-11F";
                default: return "";
            }
        }

        private bool MatchesAirScoutOpportunityFilter(KstUserInfo user)
        {
            if (user == null || _settings == null || !_settings.AirScoutEnabled || IsWatchedCall(user.Call)) return true;
            if (_settings.AirScoutFilterMinutes < 0 && _settings.AircraftFilterMode == (int)KstAircraftFilterMode.All) return true;

            AirScoutPathResult result;
            if (!_airScoutResults.TryGetValue(CleanCall(user.Call), out result) || result == null) return false;
            AirScoutPlaneInfo best = GetBestFilteredAirScoutPlane(result);
            if (best == null) return false;
            if (_settings.AirScoutFilterMinutes < 0) return true;
            if (_settings.AirScoutFilterMinutes == 0) return best.Mins <= 0;
            return best.Mins >= 0 && best.Mins <= _settings.AirScoutFilterMinutes;
        }

        private bool IsUserVisibleForCurrentFilter(KstUserInfo user)
        {
            if (user == null) return false;
            if (_settings != null && _settings.UnworkedCurrentBandOnly && user.WorkedCurrentBand && !IsWatchedCall(user.Call)) return false;
            return IsUserWithinDistanceFilter(user) && MatchesAirScoutOpportunityFilter(user);
        }

        private void RebuildVisibleUserList()
        {
            if (_users == null) return;
            List<KstUserInfo> users = new List<KstUserInfo>(_userMap.Values);
            _rebuildingVisibleUserList = true;
            try
            {
                _users.BeginUpdate();
                _users.Items.Clear();
                foreach (KstUserInfo user in users)
                {
                    if (user != null && IsUserVisibleForCurrentFilter(user)) UpsertUser(user);
                }
            }
            finally
            {
                _rebuildingVisibleUserList = false;
                try { _users.EndUpdate(); } catch { }
            }

            if (!String.IsNullOrWhiteSpace(_lastSelectedCall) && FindUserItem(_lastSelectedCall) == null)
                _lastSelectedCall = "";
            SortUsers();
            UpdateComposeTarget();
            RefreshConversationView();
            RefreshMapWindow();
        }

        private void PopulateRoomCombo(ComboBox combo, int selectedRoom)
        {
            if (combo == null) return;
            _applyingRoomSelection = true;
            try
            {
                combo.BeginUpdate();
                combo.Items.Clear();
                foreach (KstRoomOption option in KstRoomTitles.GetOptions()) combo.Items.Add(option);
                int selectedIndex = -1;
                for (int i = 0; i < combo.Items.Count; i++)
                {
                    KstRoomOption option = combo.Items[i] as KstRoomOption;
                    if (option != null && option.Room == selectedRoom) { selectedIndex = i; break; }
                }
                if (selectedIndex < 0)
                {
                    KstRoomOption custom = new KstRoomOption(selectedRoom, KstRoomTitles.GetTitle(selectedRoom));
                    combo.Items.Add(custom);
                    selectedIndex = combo.Items.Count - 1;
                }
                combo.SelectedIndex = selectedIndex;
            }
            finally
            {
                combo.EndUpdate();
                _applyingRoomSelection = false;
            }
        }

        private void PrepareCqCompose()
        {
            if (_kst == null || !_kst.IsConnected) return;
            ClearSelectedStation();
            UpdateComposeTarget();
            CaptureInputFocus(_composeBox);
            UpdateStatus("Compose a general KST message and press Send or Enter");
        }

        private void UpdateComposeTarget()
        {
            if (_composeTargetLabel == null) return;
            string call = GetHighlightedCall();
            _composeTargetLabel.Text = String.IsNullOrWhiteSpace(call) ? "CQ" : "To: " + CleanCall(call);
            if (_composeBox != null)
                _composeBox.AccessibleDescription = String.IsNullOrWhiteSpace(call) ? "General KST message" : "Message to " + CleanCall(call);
        }

        private async Task SendComposeClicked()
        {
            if (_kst == null || !_kst.IsConnected || _composeBox == null) return;
            string body = (_composeBox.Text ?? "").Trim();
            if (body.Length == 0)
            {
                UpdateStatus("Type a message first");
                CaptureInputFocus(_composeBox);
                return;
            }

            string call = GetHighlightedCall();
            if (String.IsNullOrWhiteSpace(call))
            {
                await _kst.SendCommandAsync(body);
                UpdateStatus("Sent general KST message");
            }
            else
            {
                await SendDirectedMessageAsync(call, body);
                UpdateStatus("Sent directed KST message to " + CleanCall(call));
            }
            _composeBox.Clear();
            CaptureInputFocus(_composeBox);
        }

        private void UpdateMacroToolTip(int index)
        {
            if (_macroToolTip == null || _macroButtons == null || index < 0 || index >= _macroButtons.Length) return;
            string call = GetHighlightedCall();
            string[] activeMacros = GetActiveMacroSet();
            string template = activeMacros != null && index < activeMacros.Length ? (activeMacros[index] ?? "") : "";
            string preview = String.IsNullOrWhiteSpace(template) ? "Macro is empty" : ExpandMacro(template, String.IsNullOrWhiteSpace(call) ? "CALL" : call);
            string target = String.IsNullOrWhiteSpace(call) ? "Select a station before sending" : "Will send to " + CleanCall(call);
            _macroToolTip.SetToolTip(_macroButtons[index], target + Environment.NewLine + preview + Environment.NewLine + "Right-click to edit M" + (index + 1).ToString());
        }

        private void RestyleUserCall(string call)
        {
            if (_users == null || String.IsNullOrWhiteSpace(call)) return;
            foreach (ListViewItem item in _users.Items)
            {
                if (!String.Equals(CleanCall(item.Text), CleanCall(call), StringComparison.OrdinalIgnoreCase)) continue;
                StyleUserItem(item, item.Tag as KstUserInfo);
                _users.Invalidate(item.Bounds);
                break;
            }
        }

        private void SetupClicked()
        {
            KstSettings oldSettings = _settings != null ? _settings.Clone() : new KstSettings();
            KstSetupDialog dlg = new KstSetupDialog(_settings);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _settings = dlg.Settings;
                ApplySettingsToUi();
                _settings.Save();
                if (!WorkedBandColumnsEqual(oldSettings.WorkedBandColumns, _settings.WorkedBandColumns)) RebuildUserColumns();
                RebuildVisibleUserList();
                ConfigureAirScoutClient();
                if (_kst != null && _kst.IsConnected)
                {
                    // ON4KST only reliably publishes your displayed name/locator during login/room join.
                    // /SET NA and /SET QRA may update the server, but other clients and /SH US can still
                    // show the old values until this client logs out and back in, so reconnect automatically.
                    if (SettingsRequireReconnect(oldSettings, _settings))
                        _ = ReconnectAfterSetupChangeAsync();
                    else
                        _ = ApplyOwnProfileToKst(oldSettings);
                }
            }
        }

        private static bool WorkedBandColumnsEqual(string[] a, string[] b)
        {
            string left = String.Join(",", a ?? new string[0]);
            string right = String.Join(",", b ?? new string[0]);
            return String.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SettingsRequireReconnect(KstSettings oldSettings, KstSettings newSettings)
        {
            if (oldSettings == null || newSettings == null) return false;
            if (!String.Equals(oldSettings.Host ?? "", newSettings.Host ?? "", StringComparison.OrdinalIgnoreCase)) return true;
            if (oldSettings.Port != newSettings.Port) return true;
            if (oldSettings.Room != newSettings.Room) return true;
            if (!String.Equals(oldSettings.Callsign ?? "", newSettings.Callsign ?? "", StringComparison.OrdinalIgnoreCase)) return true;
            if (!String.Equals(oldSettings.Password ?? "", newSettings.Password ?? "", StringComparison.Ordinal)) return true;
            if (!String.Equals((oldSettings.Name ?? "").Trim(), (newSettings.Name ?? "").Trim(), StringComparison.Ordinal)) return true;
            if (!String.Equals(NormalizeLocator(oldSettings.OwnLocator ?? ""), NormalizeLocator(newSettings.OwnLocator ?? ""), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private async Task ReconnectAfterSetupChangeAsync()
        {
            try
            {
                UpdateStatus("KST setup changed - reconnecting so name/locator/room are refreshed...");
                DisconnectClicked();
                await Task.Delay(600);
                await ConnectClicked();
            }
            catch (Exception ex)
            {
                UpdateStatus("KST reconnect failed: " + ex.Message);
            }
        }

        private async Task ChangeRoomClicked()
        {
            KstRoomOption option = _roomCombo != null ? _roomCombo.SelectedItem as KstRoomOption : null;
            if (option == null) return;
            int newRoom = option.Room;
            if (_settings == null) _settings = new KstSettings();
            if (_settings.Room == newRoom) return;

            _settings.Room = newRoom;
            _settings.Save();
            _pendingUserSnapshot.Clear();
            _refreshingUserList = false;
            _userMap.Clear();
            if (_users != null) _users.Items.Clear();
            if (_messages != null) _messages.Items.Clear();
            if (_threadMessages != null) _threadMessages.Items.Clear();
            _unreadDirectedByCall.Clear();
            ResetAirScoutAutoScan(true);
            RefreshMapWindow();

            if (_kst != null && _kst.IsConnected)
            {
                UpdateStatus("Changing KST room to " + KstRoomTitles.GetTitle(_settings.Room) + "...");
                DisconnectClicked();
                await ConnectClicked();
            }
            else
            {
                UpdateStatus("KST room set to " + KstRoomTitles.GetTitle(_settings.Room));
            }
        }

        private string GetActiveMacroProfileKey()
        {
            if (_settings != null && _settings.Room == 4) return "eme";
            string band = KstWorkedBands.NormalizeKey(GetCurrentWorkedBandKey());
            if (band == "50" || band == "70") return "50_70";
            if (band == "144" || band == "432") return "144_432";
            if (band == "1296") return "1296";
            if (band == "2320" || band == "3400" || band == "5760" || band == "10368" || band == "24048" || band == "47088" || band == "76032") return "microwave";
            return "general";
        }

        private static string GetMacroProfileTitle(string profile)
        {
            if (profile == "50_70") return "50/70 MHz";
            if (profile == "144_432") return "144/432 MHz";
            if (profile == "1296") return "1296 MHz";
            if (profile == "microwave") return "Microwave";
            if (profile == "eme") return "EME";
            return "General";
        }

        private string[] GetActiveMacroSet()
        {
            if (_settings == null) return new string[] { "", "", "", "" };
            return _settings.GetMacroSet(GetActiveMacroProfileKey());
        }

        private void SetMacroSet(string profile, string[] macros)
        {
            if (_settings == null) return;
            _settings.SetMacroSet(profile, macros);
            _settings.Save();
        }

        private void EditMacrosClicked()
        {
            EditMacrosClicked(0);
        }

        private void EditMacrosClicked(int focusIndex)
        {
            string profile = GetActiveMacroProfileKey();
            KstMacroDialog dlg = new KstMacroDialog(GetActiveMacroSet(), focusIndex, GetMacroProfileTitle(profile));
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                SetMacroSet(profile, dlg.Macros);
                for (int i = 0; i < 4; i++) UpdateMacroToolTip(i);
                UpdateStatus("KST macros saved");
            }
        }

        private async Task SendMacroClicked(int index)
        {
            if (_kst == null || !_kst.IsConnected) return;
            string[] activeMacros = GetActiveMacroSet();
            if (activeMacros == null || index < 0 || index >= activeMacros.Length) return;
            string call = GetHighlightedCall();
            if (String.IsNullOrWhiteSpace(call))
            {
                UpdateStatus("Highlight a station first before sending a macro.");
                return;
            }
            string template = activeMacros[index] ?? "";
            string body = ExpandMacro(template, call).Trim();
            if (body.Length == 0)
            {
                UpdateStatus("Macro " + (index + 1).ToString() + " is empty. Right-click the macro button to edit it.");
                return;
            }
            await SendDirectedMessageAsync(call, body);
            UpdateStatus("Sent M" + (index + 1).ToString() + " to " + call.ToUpperInvariant());
        }

        private void ApplySettingsToUi()
        {
            _hostBox.Text = _settings.Host;
            _portBox.Value = Math.Max(_portBox.Minimum, Math.Min(_portBox.Maximum, _settings.Port));
            PopulateRoomCombo(_roomCombo, _settings.Room);
            PopulateDistanceCombo(_distanceCombo, _settings.DistanceFilterKm);
            PopulateAirScoutFilterCombo(_airScoutFilterCombo, _settings.AirScoutFilterMinutes);
            PopulateAircraftFilterCombo(_aircraftFilterCombo, _settings.AircraftFilterMode);
            if (_airScoutFilterCombo != null) _airScoutFilterCombo.Enabled = _settings.AirScoutEnabled;
            if (_aircraftFilterCombo != null) _aircraftFilterCombo.Enabled = _settings.AirScoutEnabled;
            if (_airScoutAutoSortCheck != null) { _airScoutAutoSortCheck.Checked = _settings.AirScoutAutoSort; _airScoutAutoSortCheck.Enabled = _settings.AirScoutEnabled; }
            if (_unworkedOnlyCheck != null) _unworkedOnlyCheck.Checked = _settings.UnworkedCurrentBandOnly;
            LoadWatchedCallsFromSettings();
            _userBox.Text = _settings.Callsign;
            _passBox.Text = _settings.Password;
            UpdateComposeTarget();
        }

        private async Task ConnectClicked()
        {
            try
            {
                _settings.Host = _hostBox.Text.Trim();
                _settings.Port = (int)_portBox.Value;
                // Room is selected in Setup; top row displays the room title.
                _settings.Callsign = _userBox.Text.Trim().ToUpperInvariant();
                _settings.Password = _passBox.Text;

                if (String.IsNullOrWhiteSpace(_settings.Callsign) || String.IsNullOrWhiteSpace(_settings.Password))
                {
                    SetupClicked();
                    if (String.IsNullOrWhiteSpace(_settings.Callsign) || String.IsNullOrWhiteSpace(_settings.Password))
                    {
                        UpdateStatus("Enter KST username and password first.");
                        return;
                    }
                }

                _settings.Save();
                _messages.Items.Clear();
                if (_threadMessages != null) _threadMessages.Items.Clear();
                _unreadDirectedByCall.Clear();
                _users.Items.Clear();
                _pendingUserSnapshot.Clear();
                _refreshingUserList = false;
                _userMap.Clear();

                _kstLoginComplete = false;
                _kstDisconnectRequested = false;
                _kstFailureHandling = false;
                _kstFailurePopupShown = false;
                if (_kstLoginTimeoutTimer != null) _kstLoginTimeoutTimer.Stop();
                DisposeKstClient();
                SetConnectingUi();

                _kst = new TelnetKstClient(_settings.Host, _settings.Port, _settings.Callsign, _settings.Password, _settings.Room);
                _kst.LineReceived += OnKstLineReceived;
                _kst.StatusChanged += OnKstStatusChanged;
                _kst.LoggedIn += OnKstLoggedIn;

                await _kst.ConnectAsync();
                if (_kst == null || _kstDisconnectRequested) return;
                if (!_kstLoginComplete)
                {
                    if (_kstLoginTimeoutTimer != null) _kstLoginTimeoutTimer.Start();
                    UpdateStatus("Connected to ON4KST - waiting for login to complete");
                }
            }
            catch (Exception ex)
            {
                HandleKstConnectionFailure(
                    "KST connection failed: " + ex.Message,
                    "The bridge could not connect to ON4KST.\r\n\r\n" + ex.Message + "\r\n\r\nCheck the internet connection and KST settings, then try Connect again.");
            }
        }

        private void DisconnectClicked()
        {
            _kstDisconnectRequested = true;
            _kstLoginComplete = false;
            if (_kstLoginTimeoutTimer != null) _kstLoginTimeoutTimer.Stop();
            try
            {
                if (_kst != null && _kst.IsConnected) _kst.SendCommandAsync("/QUIT");
            }
            catch { }

            DisposeKstClient();
            _userRefreshTimer.Stop();
            SetConnectionUi(false);
            UpdateStatus("Disconnected");
        }

        private async Task SendClicked(bool forceDialog)
        {
            if (_kst == null || !_kst.IsConnected) return;

            string call = GetHighlightedCall();
            if (!String.IsNullOrWhiteSpace(call))
            {
                await SendMessageToCall(call, "");
                return;
            }

            if (_composeDialogOpen) return;

            string initial = "";

            string line;
            try
            {
                _composeDialogOpen = true;
                line = MessagePrompt.Show(this, "Send general KST message", "Message", initial);
            }
            finally
            {
                _composeDialogOpen = false;
            }

            if (line == null) return;
            line = line.Trim();
            if (line.Length == 0) return;
            await _kst.SendCommandAsync(line);
            UpdateStatus("Sent general KST message");
            ResetSendBoxPlaceholder();
        }

        private async Task SendMessageToCall(string call, string initial)
        {
            if (_kst == null || !_kst.IsConnected || String.IsNullOrWhiteSpace(call)) return;
            call = CleanCall(call);
            if (String.IsNullOrWhiteSpace(call)) return;
            if (_composeDialogOpen) return;

            string body;
            try
            {
                _composeDialogOpen = true;
                body = MessagePrompt.Show(this, "Send KST message to " + call, "Message to " + call, initial ?? "");
            }
            finally
            {
                _composeDialogOpen = false;
            }

            if (body == null) return;
            body = body.Trim();
            if (body.Length == 0) return;
            await SendDirectedMessageAsync(call, body);
            UpdateStatus("Sent directed KST message to " + call.ToUpperInvariant());
            ResetSendBoxPlaceholder();
        }

        private async Task CqClicked()
        {
            if (_kst == null || !_kst.IsConnected) return;

            // CQ is now the explicit general-message action.  Clear any highlighted
            // station/message first so the user can always send to CQ even after
            // selecting a station in the list.
            ClearSelectedStation();

            if (_composeDialogOpen) return;
            string line;
            try
            {
                _composeDialogOpen = true;
                line = MessagePrompt.Show(this, "Send CQ / general KST message", "CQ message", "");
            }
            finally
            {
                _composeDialogOpen = false;
            }

            if (line == null) return;
            line = line.Trim();
            if (line.Length == 0) return;
            await _kst.SendCommandAsync(line);
            UpdateStatus("Sent CQ/general KST message");
            ResetSendBoxPlaceholder();
        }

        private async Task ToCallClicked()
        {
            if (_kst == null || !_kst.IsConnected) return;
            string call = GetHighlightedCall();
            if (String.IsNullOrWhiteSpace(call))
            {
                UpdateStatus("Highlight a station first.");
                return;
            }

            // Blank directed compose field.  Macros remain the pre-filled quick-send options.
            await SendMessageToCall(call, "");
        }

        private void ClearSelectedStation()
        {
            string previousUser = _lastStyledUserCall;
            ListViewItem previousMessage = _lastStyledMessageItem;
            ListViewItem previousThreadMessage = _lastStyledThreadItem;

            ClearListSelection(_users);
            ClearListSelection(_messages);
            ClearListSelection(_threadMessages);
            _lastSelectedCall = "";
            _lastStyledUserCall = "";
            _lastStyledMessageItem = null;
            _lastStyledThreadItem = null;

            RestyleUserCall(previousUser);
            RestyleMessageItem(previousMessage);
            RestyleMessageItem(previousThreadMessage);
            RefreshConversationView();
            UpdateComposeTarget();
            RefreshMapWindow();
        }

        private static void ClearListSelection(ListView list)
        {
            if (list == null) return;
            List<ListViewItem> selected = new List<ListViewItem>();
            foreach (ListViewItem item in list.SelectedItems) selected.Add(item);
            foreach (ListViewItem item in selected) item.Selected = false;
        }

        private void ResetSendBoxPlaceholder()
        {
            // Inline compose is active. Placeholder handling is retained only for compatibility
            // with older call paths that still use the modal message prompt.
        }

        private async Task RefreshUsers()
        {
            if (_kst == null || !_kst.IsConnected) return;

            if (_refreshingUserList)
            {
                // Never stack /SH US requests. A missing prompt used to leave BeginUpdate
                // active and made the station list appear blank indefinitely.
                if ((DateTime.UtcNow - _userRefreshStartedUtc).TotalSeconds < 8.0) return;
                AbortUserRefresh("KST user-list refresh timed out; keeping the existing list");
            }

            _pendingUserSnapshot.Clear();
            _refreshingUserList = true;
            _userRefreshStartedUtc = DateTime.UtcNow;
            await _kst.SendCommandAsync("/SH US");
            UpdateStatus("Requested KST user list");
        }

        private void AbortUserRefresh(string status)
        {
            _pendingUserSnapshot.Clear();
            _refreshingUserList = false;
            _userRefreshStartedUtc = DateTime.MinValue;
            if (!String.IsNullOrWhiteSpace(status)) UpdateStatus(status);
        }

        private void OnKstLoggedIn(object sender, EventArgs e)
        {
            SafeUi(async delegate
            {
                if (_kst == null || !Object.ReferenceEquals(sender, _kst)) return;
                _kstLoginComplete = true;
                _kstDisconnectRequested = false;
                if (_kstLoginTimeoutTimer != null) _kstLoginTimeoutTimer.Stop();
                SetConnectionUi(true);
                UpdateStatus("Logged in - " + KstRoomTitles.GetTitle(_settings.Room));
                _userRefreshTimer.Start();
                await ApplyOwnProfileToKst(null);
                await RefreshUsers();
            });
        }

        private void OnKstStatusChanged(object sender, string status)
        {
            SafeUi(delegate
            {
                if (_kst != null && sender != null && !Object.ReferenceEquals(sender, _kst)) return;
                UpdateStatus(status);
                if (_kstDisconnectRequested || _kstFailureHandling || String.IsNullOrWhiteSpace(status)) return;

                if (!_kstLoginComplete && IsKstLoginFailureStatus(status))
                {
                    HandleKstConnectionFailure(
                        status,
                        "ON4KST rejected the login.\r\n\r\n" + status + "\r\n\r\nCheck the callsign and password in Setup, then press Connect again.");
                    return;
                }

                if (!_kstLoginComplete && status.Equals("KST connection closed.", StringComparison.OrdinalIgnoreCase))
                {
                    HandleKstConnectionFailure(
                        "KST connection closed before login completed",
                        "ON4KST closed the connection before the bridge completed login.\r\n\r\nCheck the callsign, password, selected room and internet connection, then press Connect again.");
                    return;
                }

                if (_kstLoginComplete &&
                    (status.Equals("KST connection closed.", StringComparison.OrdinalIgnoreCase) ||
                     status.StartsWith("KST read error:", StringComparison.OrdinalIgnoreCase) ||
                     status.StartsWith("Disconnected by KST server", StringComparison.OrdinalIgnoreCase)))
                {
                    HandleKstConnectionLost(status);
                }
            });
        }

        private void OnKstLineReceived(object sender, string line)
        {
            SafeUi(delegate
            {
                if (_kst == null || !Object.ReferenceEquals(sender, _kst)) return;
                ApplyKstLine(line);
            });
        }

        private void ApplyKstLine(string line)
        {
            if (String.IsNullOrWhiteSpace(line)) return;
            KstParsedLine parsed = KstTextParser.Parse(_settings.Callsign, line);
            if (parsed.Type == KstParsedType.User)
            {
                KstUserInfo incoming = new KstUserInfo
                {
                    Call = parsed.Call,
                    Name = parsed.Name,
                    Locator = parsed.Locator,
                    Dirty = false,
                    MissedRefreshes = 0
                };
                string clean = CleanCall(incoming.Call);
                if (_refreshingUserList && !String.IsNullOrWhiteSpace(clean))
                    _pendingUserSnapshot[clean] = incoming;

                // Merge rows as they arrive. The old list remains painted throughout
                // the network request, so a slow or incomplete response cannot blank it.
                UpsertUser(incoming);
                return;
            }

            if (parsed.Type == KstParsedType.Chat || parsed.Type == KstParsedType.DxClusterSpot)
            {
                AddChatMessage(parsed);
                return;
            }

            if (parsed.Type == KstParsedType.Prompt)
            {
                CleanupUserList();
                return;
            }
        }

        private void CleanupUserList()
        {
            if (!_refreshingUserList) return;

            try { if (_users != null) _users.BeginUpdate(); } catch { }
            try
            {
                if (_pendingUserSnapshot.Count == 0)
                {
                    // A bare prompt, rate-limit response or interrupted transfer is not
                    // evidence that the room is empty. Preserve every current row.
                    UpdateStatus("KST returned no complete user list; keeping the existing list");
                }
                else
                {
                    List<string> remove = new List<string>();
                    foreach (KeyValuePair<string, KstUserInfo> kv in _userMap)
                    {
                        if (_pendingUserSnapshot.ContainsKey(kv.Key))
                        {
                            kv.Value.MissedRefreshes = 0;
                        }
                        else
                        {
                            kv.Value.MissedRefreshes++;
                            // Require three consecutive completed snapshots before
                            // removing a station. This filters transient/partial KST replies.
                            if (kv.Value.MissedRefreshes >= 3) remove.Add(kv.Key);
                        }
                    }
                    foreach (string call in remove) RemoveUser(call);
                    UpdateStatus("KST user list updated: " + _pendingUserSnapshot.Count.ToString() + " stations");
                }

                UpdateWorkedFlags();
                SortUsers();
            }
            finally
            {
                if (_userRefreshStartedUtc != DateTime.MinValue)
                    _perfLastKstRefreshMs = Math.Max(0, (long)(DateTime.UtcNow - _userRefreshStartedUtc).TotalMilliseconds);
                _pendingUserSnapshot.Clear();
                _refreshingUserList = false;
                _userRefreshStartedUtc = DateTime.MinValue;
                try { if (_users != null) _users.EndUpdate(); } catch { }
            }

            // Do not abort an AirScout pass simply because the normal /SH US
            // refresh completed. A full room can take longer than the ten-second
            // KST refresh period to scan. Finish the current pass, then rebuild
            // the queue from the latest user snapshot.
            RequestAirScoutRescanForLatestUsers();
            RefreshMapWindow();

            if (_forceUserRefreshAfterCurrent)
            {
                _forceUserRefreshAfterCurrent = false;
                try
                {
                    BeginInvoke(new MethodInvoker(delegate { _ = RefreshUsersForBandChangeAsync(); }));
                }
                catch { }
            }
        }

        private void AddChatMessage(KstParsedLine msg)
        {
            if (msg == null) return;
            if (!String.IsNullOrWhiteSpace(msg.Call) && !_userMap.ContainsKey(msg.Call))
                UpsertUser(new KstUserInfo { Call = msg.Call, Name = msg.Name, Locator = "", Dirty = false, LastActivityUtc = DateTime.UtcNow });
            KstUserInfo activeUser = null;
            if (!String.IsNullOrWhiteSpace(msg.Call) && _userMap.TryGetValue(CleanCall(msg.Call), out activeUser))
            {
                activeUser.LastActivityUtc = DateTime.UtcNow;
                UpdateUserInfoCells(activeUser.Call);
                if (_userSortColumn == 6) SortUsers();
            }

            bool worked = activeUser != null && activeUser.WorkedCurrentBand;
            msg.Worked = worked;
            bool directToMe = IsDirectToMe(msg);
            ListViewItem item = CreateMessageItem(msg);
            _messages.Items.Add(item);
            // Incoming chat must never select a station implicitly.  The current
            // map trace, AirScout path and compose target are operator choices and
            // change only after a deliberate station selection or Prepare contact.

            if (_messages.Items.Count > 1500) _messages.Items.RemoveAt(0);
            if (_messages.Items.Count > 0) _messages.EnsureVisible(_messages.Items.Count - 1);
            RefreshConversationView();

            if (directToMe)
            {
                string fromCall = CleanCall(msg.Call);
                string currentConversation = CleanCall(GetConversationCall());
                bool alreadyViewing = Visible && ContainsFocus && String.Equals(fromCall, currentConversation, StringComparison.OrdinalIgnoreCase);
                if (!alreadyViewing)
                {
                    int unread;
                    _unreadDirectedByCall.TryGetValue(fromCall, out unread);
                    _unreadDirectedByCall[fromCall] = unread + 1;
                    UpdateUserInfoCells(fromCall);
                }
                try { if (_mainForm != null) _mainForm.SetMainStatusText("DIRECT KST msg de " + msg.Call + ": " + msg.Message); } catch { }
            }
        }

        private void AddSystemMessage(string text)
        {
            KstParsedLine sys = new KstParsedLine { Type = KstParsedType.Prompt, TimeText = DateTime.UtcNow.ToString("HH:mm"), Call = "SYSTEM", Name = "", Message = text };
            ListViewItem item = CreateMessageItem(sys);
            _messages.Items.Add(item);
            if (_messages.Items.Count > 1500) _messages.Items.RemoveAt(0);
            if (_messages.Items.Count > 0) _messages.EnsureVisible(_messages.Items.Count - 1);
        }

        private ListViewItem CreateMessageItem(KstParsedLine msg)
        {
            ListViewItem item = new ListViewItem(msg.TimeText ?? DateTime.UtcNow.ToString("HH:mm"));
            item.SubItems.Add(msg.Call ?? "");
            item.SubItems.Add(msg.Name ?? "");
            item.SubItems.Add(msg.Message ?? "");
            item.Tag = msg;
            StyleMessageItem(item, msg, msg != null && msg.Worked);
            return item;
        }

        private void RefreshConversationView()
        {
            if (_threadMessages == null || _messages == null) return;
            string call = GetConversationCall();
            _threadMessages.BeginUpdate();
            try
            {
                _threadMessages.Items.Clear();
                if (_threadHeaderLabel != null)
                    _threadHeaderLabel.Text = String.IsNullOrWhiteSpace(call) ? "Selected station messages" : "Messages with " + call.ToUpperInvariant();

                if (!String.IsNullOrWhiteSpace(call))
                {
                    string clean = CleanCall(call);
                    foreach (ListViewItem source in _messages.Items)
                    {
                        KstParsedLine msg = source.Tag as KstParsedLine;
                        if (IsConversationMessage(msg, clean))
                            _threadMessages.Items.Add(CreateMessageItem(msg));
                    }
                    if (_threadMessages.Items.Count > 0)
                        _threadMessages.EnsureVisible(_threadMessages.Items.Count - 1);
                }
            }
            finally
            {
                _threadMessages.EndUpdate();
            }
        }

        private string GetConversationCall()
        {
            if (_users != null && _users.SelectedItems.Count > 0) return _users.SelectedItems[0].Text;
            if (_messages != null && _messages.SelectedItems.Count > 0)
            {
                KstParsedLine msg = _messages.SelectedItems[0].Tag as KstParsedLine;
                string call = GetOtherPartyForMessage(msg);
                if (!String.IsNullOrWhiteSpace(call)) return call;
            }
            if (!String.IsNullOrWhiteSpace(_lastSelectedCall)) return _lastSelectedCall;
            return "";
        }

        private string GetOtherPartyForMessage(KstParsedLine msg)
        {
            if (msg == null) return "";
            string myCall = _settings == null ? "" : CleanCall(_settings.Callsign);
            string from = CleanCall(msg.Call);
            string target = CleanCall(GetDirectedTarget(msg.Message));
            if (!String.IsNullOrWhiteSpace(myCall) && String.Equals(from, myCall, StringComparison.OrdinalIgnoreCase) && !String.IsNullOrWhiteSpace(target)) return target;
            return from;
        }

        private bool IsConversationMessage(KstParsedLine msg, string cleanCall)
        {
            if (msg == null || String.IsNullOrWhiteSpace(cleanCall)) return false;
            if (msg.Type == KstParsedType.Prompt) return false;
            string from = CleanCall(msg.Call);
            if (String.Equals(from, cleanCall, StringComparison.OrdinalIgnoreCase)) return true;
            string target = CleanCall(GetDirectedTarget(msg.Message));
            if (String.Equals(target, cleanCall, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void UpdateLastSelectedCallFromMessageList(ListView list)
        {
            if (list == null || list.SelectedItems.Count == 0) return;
            KstParsedLine msg = list.SelectedItems[0].Tag as KstParsedLine;
            string call = GetOtherPartyForMessage(msg);
            if (!String.IsNullOrWhiteSpace(call)) _lastSelectedCall = call;
        }

        private void UpsertUser(KstUserInfo user)
        {
            if (user == null || String.IsNullOrWhiteSpace(user.Call)) return;
            user.Call = CleanCall(user.Call);
            if (String.IsNullOrWhiteSpace(user.Call)) return;
            user.IsWatched = IsWatchedCall(user.Call);
            KstUserInfo existing;
            if (_userMap.TryGetValue(user.Call, out existing))
            {
                if (String.IsNullOrWhiteSpace(user.Name)) user.Name = existing.Name;
                if (String.IsNullOrWhiteSpace(user.Locator)) user.Locator = existing.Locator;
                user.WorkedCurrentBand = existing.WorkedCurrentBand;
                user.WorkedBandKey = existing.WorkedBandKey;
                user.WorkedCheckComplete = existing.WorkedCheckComplete;
                user.MissedRefreshes = existing.MissedRefreshes;
                if (user.LastActivityUtc == DateTime.MinValue) user.LastActivityUtc = existing.LastActivityUtc;
            }
            _userMap[user.Call] = user;

            ListViewItem item = null;
            foreach (ListViewItem it in _users.Items)
            {
                if (String.Equals(it.Text, user.Call, StringComparison.OrdinalIgnoreCase)) { item = it; break; }
            }

            if (!IsUserVisibleForCurrentFilter(user))
            {
                if (item != null) _users.Items.Remove(item);
                if (!_refreshingUserList && !_rebuildingVisibleUserList) RefreshMapWindow();
                return;
            }

            string qtf = "";
            string qrb = "";
            CalculateQtfQrb(user.Locator, out qtf, out qrb);

            if (item == null)
            {
                item = new ListViewItem(user.Call);
                item.SubItems.Add(user.Name ?? "");
                item.SubItems.Add(user.Locator ?? "");
                item.SubItems.Add(qtf);
                item.SubItems.Add(qrb);
                item.SubItems.Add(GetAirScoutDisplay(user.Call));
                item.SubItems.Add(GetLastActiveDisplay(user));
                item.SubItems.Add(GetUnreadDisplay(user.Call));
                foreach (KstWorkedBandOption band in GetVisibleWorkedBandOptions())
                    item.SubItems.Add(GetWorkedBandCell(user.Call, band.Key));
                item.Tag = user;
                UpdateAirScoutItemToolTip(item);
                StyleUserItem(item, user);
                _users.Items.Add(item);
            }
            else
            {
                item.SubItems[1].Text = user.Name ?? "";
                item.SubItems[2].Text = user.Locator ?? "";
                item.SubItems[3].Text = qtf;
                item.SubItems[4].Text = qrb;
                EnsureUserSubItems(item);
                item.SubItems[UserColAirScout].Text = GetAirScoutDisplay(user.Call);
                item.SubItems[UserColActive].Text = GetLastActiveDisplay(user);
                item.SubItems[UserColUnread].Text = GetUnreadDisplay(user.Call);
                List<KstWorkedBandOption> visibleBands = GetVisibleWorkedBandOptions();
                for (int i = 0; i < visibleBands.Count; i++)
                    item.SubItems[UserColFirstWorkedBand + i].Text = GetWorkedBandCell(user.Call, visibleBands[i].Key);
                item.Tag = user;
                UpdateAirScoutItemToolTip(item);
                StyleUserItem(item, user);
            }
            QueueWorkedCheck(user.Call);
            if (!_refreshingUserList && !_rebuildingVisibleUserList)
            {
                SortUsers();
                RefreshMapWindow();
            }
        }

        private void RemoveUser(string call)
        {
            if (String.IsNullOrWhiteSpace(call)) return;
            call = CleanCall(call);
            _userMap.Remove(call);
            foreach (ListViewItem it in _users.Items)
            {
                if (String.Equals(it.Text, call, StringComparison.OrdinalIgnoreCase))
                {
                    _users.Items.Remove(it);
                    break;
                }
            }
            if (!_refreshingUserList) RefreshMapWindow();
        }

        private void RecalculateAllUsers()
        {
            if (_users == null) return;
            foreach (ListViewItem item in _users.Items)
            {
                KstUserInfo user = item.Tag as KstUserInfo;
                if (user == null) continue;
                string qtf;
                string qrb;
                CalculateQtfQrb(user.Locator, out qtf, out qrb);
                if (item.SubItems.Count > 3) item.SubItems[3].Text = qtf;
                if (item.SubItems.Count > 4) item.SubItems[4].Text = qrb;
                user.WorkedCurrentBand = IsWorkedBefore(user.Call);
                UpdateUserInfoCells(item, user);
                StyleUserItem(item, user);
                UpdateAirScoutItemToolTip(item);
            }
            SortUsers();
            RefreshMapWindow();
        }

        private void UsersColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (_users == null || e.Column < 0 || e.Column >= _users.Columns.Count || e.Column == UserColName) return;

            if (_userSortColumn == e.Column)
                _userSortOrder = (_userSortOrder == SortOrder.Ascending) ? SortOrder.Descending : SortOrder.Ascending;
            else
            {
                _userSortColumn = e.Column;
                _userSortOrder = SortOrder.Ascending;
            }

            SortUsers();
            UpdateStatus("Sorted KST users by " + _users.Columns[_userSortColumn].Text.Replace(" ▲", "").Replace(" ▼", "") + " " + (_userSortOrder == SortOrder.Ascending ? "ascending" : "descending"));
        }

        private void SortUsers()
        {
            if (_users == null || _userSortColumn < 0 || _users.Items.Count < 2) return;
            _users.ListViewItemSorter = new KstUserListComparer(_userSortColumn, _userSortOrder);
            _users.Sort();
            _users.ListViewItemSorter = null;
            UpdateUserColumnHeaders();
            RestyleUsers();
        }

        private void SortUsersByAirScoutOpportunity()
        {
            if (_users == null) return;
            _userSortColumn = UserColAirScout;
            _userSortOrder = SortOrder.Ascending;
            SortUsers();
        }

        private void UpdateUserColumnHeaders()
        {
            if (_users == null || _users.Columns.Count < UserColFirstWorkedBand) return;
            List<string> headers = new List<string> { "Call", "Name", "QRA", "QTF", "QRB", "AS", "Active", "Msg" };
            foreach (KstWorkedBandOption band in GetVisibleWorkedBandOptions()) headers.Add(band.Header);
            for (int i = 0; i < headers.Count && i < _users.Columns.Count; i++)
            {
                string suffix = "";
                if (i == _userSortColumn) suffix = _userSortOrder == SortOrder.Ascending ? " ▲" : " ▼";
                _users.Columns[i].Text = headers[i] + suffix;
            }
        }

        private void UsersMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ListViewItem item = _users == null ? null : _users.GetItemAt(e.X, e.Y);
            if (item == null) return;

            string call = CleanCall(item.Text);
            if (String.IsNullOrWhiteSpace(call)) return;

            DateTime now = DateTime.UtcNow;
            if (String.Equals(call, _lastManualStationActionCall, StringComparison.OrdinalIgnoreCase) &&
                (now - _lastManualStationActionUtc).TotalMilliseconds < 350.0) return;
            _lastManualStationActionCall = call;
            _lastManualStationActionUtc = now;

            string locator = GetKstLocatorForCall(call);

            // A deliberate station click is an operating action: update DXLog with
            // the latest KST QRA and, when enabled in the Map window, turn the
            // rotator after DXLog has accepted the new entry-line data.
            if (IsTurnRotatorOnStationClickEnabled())
            {
                PutCallIntoDxLog(call, locator, delegate { TurnRotatorToStation(call, locator); });
            }
            else
            {
                PutCallIntoDxLog(call, locator);
            }
        }

        private bool IsTurnRotatorOnStationClickEnabled()
        {
            try
            {
                if (_mapForm != null && !_mapForm.IsDisposed)
                    return _mapForm.TurnRotatorOnClickEnabled;
            }
            catch { }
            return _settings == null || _settings.TurnRotatorOnStationClick;
        }

        private void UsersMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            SuppressDxLogHostContextMenu();
            ListViewItem item = _users.GetItemAt(e.X, e.Y);
            if (item == null) return;
            item.Selected = true;
            ContextMenuStrip menu = MakeCallMenu(item.Text);
            menu.Show(_users, e.Location);
        }

        private void MessagesMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            SuppressDxLogHostContextMenu();
            ListView lv = sender as ListView;
            if (lv == null) lv = _messages;
            ListViewItem item = lv.GetItemAt(e.X, e.Y);
            if (item == null) return;
            item.Selected = true;
            string call = item.SubItems.Count > 1 ? item.SubItems[1].Text : "";
            KstParsedLine msg = item.Tag as KstParsedLine;
            string other = GetOtherPartyForMessage(msg);
            if (!String.IsNullOrWhiteSpace(other)) call = other;
            ContextMenuStrip menu = MakeCallMenu(call);
            menu.Show(lv, e.Location);
        }

        private ContextMenuStrip MakeCallMenu(string call)
        {
            call = CleanCall(call);
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Prepare contact", null, delegate { PrepareContact(call); });
            menu.Items.Add("Put " + call + " into DXLog", null, delegate { PutCallIntoDxLog(call, GetKstLocatorForCall(call)); });

            string rotatorLocator = GetKstLocatorForCall(call);
            int rotatorAzimuth;
            bool hasRotatorBearing = TryGetAzimuthToLocator(rotatorLocator, out rotatorAzimuth);
            ToolStripMenuItem turnRotator = new ToolStripMenuItem(
                hasRotatorBearing ? "Turn rotator to " + rotatorAzimuth.ToString() + "°" : "Turn rotator");
            turnRotator.Enabled = hasRotatorBearing;
            turnRotator.Click += delegate
            {
                string queuedCall = call;
                string queuedLocator = rotatorLocator;
                PutCallIntoDxLog(queuedCall, queuedLocator, delegate
                {
                    TurnRotatorToStation(queuedCall, queuedLocator);
                });
            };
            menu.Items.Add(turnRotator);

            menu.Items.Add("Message " + call, null, async delegate { await CqCall(call); });
            menu.Items.Add("Copy call", null, delegate { if (!String.IsNullOrWhiteSpace(call)) Clipboard.SetText(call); });
            menu.Items.Add("Send message...", null, async delegate { await SendMessageToCall(call, ""); });
            ToolStripMenuItem watch = new ToolStripMenuItem(IsWatchedCall(call) ? "Remove from watchlist" : "Add to watchlist");
            watch.Click += delegate { ToggleWatchCall(call); };
            menu.Items.Add(watch);
            ToolStripMenuItem note = new ToolStripMenuItem(String.IsNullOrWhiteSpace(GetStationNote(call)) ? "Add station note..." : "Edit station note...");
            note.Click += delegate { EditStationNote(call); };
            menu.Items.Add(note);
            if (!String.IsNullOrWhiteSpace(GetStationNote(call)))
                menu.Items.Add("Clear station note", null, delegate { SetStationNote(call, ""); });
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem showInAirScout = new ToolStripMenuItem("Show path in AirScout");
            showInAirScout.Enabled = CanQueryAirScoutForCall(call);
            showInAirScout.Click += delegate { ShowCallPathInAirScout(call); };
            menu.Items.Add(showInAirScout);
            return menu;
        }

        private void PrepareContact(string call)
        {
            call = CleanCall(call);
            if (String.IsNullOrWhiteSpace(call)) return;

            // Preparing a new contact must leave exactly one station selected.
            // Earlier versions allowed ListView multi-selection, leaving the
            // previously prepared row highlighted as well.
            ClearListSelection(_users);
            ListViewItem item = FindUserItem(call);
            if (item != null)
            {
                item.Selected = true;
                item.Focused = true;
                try { item.EnsureVisible(); } catch { }
            }
            _lastSelectedCall = call;
            MarkCallRead(call);

            string kstLocator = GetKstLocatorForCall(call);
            if (IsTurnRotatorOnStationClickEnabled())
                PutCallIntoDxLog(call, kstLocator, delegate { TurnRotatorToStation(call, kstLocator); });
            else
                PutCallIntoDxLog(call, kstLocator);

            // ASSETPATH updates AirScout's calculation without sending ASSHOWPATH.
            // ASSHOWPATH deliberately raises the AirScout window, which is useful
            // for the explicit "Show path in AirScout" command but not for Prepare
            // contact while contesting in DXLog.
            QueryCallInAirScout(call, false, false);
            ShowMapWindow();
            RefreshMapWindow();
            UpdateComposeTarget();

            // Let the context menu close and allow DXLog's entry-line update/call
            // lookup to complete before returning keyboard focus to the KST editor.
            System.Windows.Forms.Timer composeFocusDelay = new System.Windows.Forms.Timer();
            composeFocusDelay.Interval = 800;
            composeFocusDelay.Tick += delegate
            {
                composeFocusDelay.Stop();
                composeFocusDelay.Dispose();
                try
                {
                    _composeBox.Focus();
                    _composeBox.SelectionStart = _composeBox.TextLength;
                    CaptureInputFocus(_composeBox);
                }
                catch { }
            };
            composeFocusDelay.Start();
            UpdateStatus("Prepared contact with " + call + ": DXLog entry, bridge map, AirScout calculation and message field ready");
        }

        private string GetStationNote(string call)
        {
            if (_settings == null || _settings.StationNotes == null) return "";
            string value;
            return _settings.StationNotes.TryGetValue(CleanCall(call), out value) ? (value ?? "") : "";
        }

        private void SetStationNote(string call, string note)
        {
            if (_settings == null) return;
            call = CleanCall(call);
            if (String.IsNullOrWhiteSpace(call)) return;
            if (_settings.StationNotes == null) _settings.StationNotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            note = (note ?? "").Trim();
            if (note.Length == 0) _settings.StationNotes.Remove(call); else _settings.StationNotes[call] = note;
            _settings.Save();
            UpdateUserRowToolTip(call);
            UpdateStatus(note.Length == 0 ? "Station note cleared for " + call : "Station note saved for " + call);
        }

        private void EditStationNote(string call)
        {
            string note = MessagePrompt.Show(this, "Station note for " + CleanCall(call), "Note", GetStationNote(call));
            if (note != null) SetStationNote(call, note);
        }

        private async Task CqCall(string call)
        {
            if (_kst == null || !_kst.IsConnected || String.IsNullOrWhiteSpace(call)) return;
            string body = MessagePrompt.Show(this, "Message " + call, "Message", BuildDefaultSkedMessage());
            if (body == null) return;
            body = body.Trim();
            if (body.Length == 0) body = BuildDefaultSkedMessage();
            await SendDirectedMessageAsync(call, body);
        }

        private void PutSelectedUserIntoDxLog()
        {
            if (_users.SelectedItems.Count == 0) return;
            ListViewItem item = _users.SelectedItems[0];
            string locator = item.SubItems.Count > 2 ? item.SubItems[2].Text : GetKstLocatorForCall(item.Text);
            PutCallIntoDxLog(item.Text, locator);
        }

        private void PutSelectedMessageIntoDxLog()
        {
            if (_messages.SelectedItems.Count == 0) return;
            KstParsedLine msg = _messages.SelectedItems[0].Tag as KstParsedLine;
            string call = GetOtherPartyForMessage(msg);
            if (String.IsNullOrWhiteSpace(call)) call = _messages.SelectedItems[0].SubItems.Count > 1 ? _messages.SelectedItems[0].SubItems[1].Text : "";
            PutCallIntoDxLog(call, GetKstLocatorForCall(call));
        }

        private void PutSelectedThreadMessageIntoDxLog()
        {
            if (_threadMessages == null || _threadMessages.SelectedItems.Count == 0) return;
            KstParsedLine msg = _threadMessages.SelectedItems[0].Tag as KstParsedLine;
            string call = GetOtherPartyForMessage(msg);
            if (String.IsNullOrWhiteSpace(call)) call = _threadMessages.SelectedItems[0].SubItems.Count > 1 ? _threadMessages.SelectedItems[0].SubItems[1].Text : "";
            PutCallIntoDxLog(call, GetKstLocatorForCall(call));
        }

        private string GetHighlightedCall()
        {
            if (_users.SelectedItems.Count > 0) return _users.SelectedItems[0].Text;
            if (_messages.SelectedItems.Count > 0)
            {
                KstParsedLine msg = _messages.SelectedItems[0].Tag as KstParsedLine;
                string call = GetOtherPartyForMessage(msg);
                if (!String.IsNullOrWhiteSpace(call)) return call;
                if (_messages.SelectedItems[0].SubItems.Count > 1) return _messages.SelectedItems[0].SubItems[1].Text;
            }
            if (_threadMessages != null && _threadMessages.SelectedItems.Count > 0)
            {
                KstParsedLine msg = _threadMessages.SelectedItems[0].Tag as KstParsedLine;
                string call = GetOtherPartyForMessage(msg);
                if (!String.IsNullOrWhiteSpace(call)) return call;
                if (_threadMessages.SelectedItems[0].SubItems.Count > 1) return _threadMessages.SelectedItems[0].SubItems[1].Text;
            }
            return "";
        }

        private string GetBestSelectedCall()
        {
            string highlighted = GetHighlightedCall();
            if (!String.IsNullOrWhiteSpace(highlighted)) return highlighted;
            return _lastSelectedCall;
        }

        private async Task SendDirectedMessageAsync(string call, string body)
        {
            if (_kst == null || !_kst.IsConnected || String.IsNullOrWhiteSpace(call) || String.IsNullOrWhiteSpace(body)) return;
            // ON4KST's telnet command for a directed message is /CQ CALL text.
            // Keep that internal so the UI can simply say "message to highlighted call".
            await _kst.SendCommandAsync("/CQ " + CleanCall(call).ToUpperInvariant() + " " + body.Trim());
        }

        private async Task ApplyOwnProfileToKst(KstSettings oldSettings)
        {
            if (_kst == null || !_kst.IsConnected || _settings == null) return;
            try
            {
                string newName = (_settings.Name ?? "").Trim();
                string newLoc = NormalizeLocator(_settings.OwnLocator ?? "");
                string oldName = oldSettings != null ? (oldSettings.Name ?? "").Trim() : null;
                string oldLoc = oldSettings != null ? NormalizeLocator(oldSettings.OwnLocator ?? "") : null;

                if (!String.IsNullOrWhiteSpace(newName) && !String.Equals(newName, oldName, StringComparison.Ordinal))
                    await _kst.SendCommandAsync("/SET NA " + newName);

                if (IsValidLocator(newLoc) && !String.Equals(newLoc, oldLoc, StringComparison.OrdinalIgnoreCase))
                    await _kst.SendCommandAsync("/SET QRA " + newLoc);
            }
            catch { }
        }

        private string BuildDefaultSkedMessage()
        {
            return ExpandMacro("PSE SKED {FREQ} {MODE}", GetBestSelectedCall());
        }

        private string ExpandMacro(string template, string call)
        {
            if (template == null) template = "";
            DxRadioSnapshot dx = GetDxRadioSnapshot();
            string result = template;
            result = result.Replace("{CALL}", (call ?? "").ToUpperInvariant());
            result = result.Replace("{MYCALL}", (_settings != null ? (_settings.Callsign ?? "") : "").ToUpperInvariant());
            result = result.Replace("{FREQ}", dx.FrequencyText);
            result = result.Replace("{FREQMHZ}", dx.FrequencyMhzText);
            result = result.Replace("{BAND}", dx.Band);
            result = result.Replace("{MODE}", dx.Mode);
            string cleanCall = CleanCall(call);
            KstUserInfo user;
            _userMap.TryGetValue(cleanCall, out user);
            string locator = user == null ? "" : NormalizeLocator(user.Locator);
            string qtf = "";
            string qrb = "";
            if (user != null) CalculateQtfQrb(user.Locator, out qtf, out qrb);
            AirScoutPathResult path;
            _airScoutResults.TryGetValue(cleanCall, out path);
            AirScoutPlaneInfo plane = path == null ? null : GetBestFilteredAirScoutPlane(path);
            string asDisplay = plane == null ? "" : (plane.Mins <= 0 ? "NOW" : plane.Mins.ToString() + "m");
            result = result.Replace("{LOC}", locator);
            result = result.Replace("{MYLOC}", NormalizeLocator(GetOwnLocator()));
            result = result.Replace("{QTF}", Regex.Match(qtf ?? "", @"\d+").Value);
            result = result.Replace("{QRB}", Regex.Match(qrb ?? "", @"\d+").Value);
            result = result.Replace("{AS}", asDisplay);
            result = result.Replace("{AIRCRAFT}", plane == null ? "" : (plane.Call ?? ""));
            result = result.Replace("{ASMIN}", plane == null ? "" : Math.Max(0, plane.Mins).ToString());
            return Regex.Replace(result, @"\s+", " ").Trim();
        }

        private DxRadioSnapshot GetDxRadioSnapshot()
        {
            DxRadioSnapshot dx = new DxRadioSnapshot();
            try
            {
                if (_contestData != null)
                {
                    dx.Band = Convert.ToString(_contestData.FocusedRadioBand) ?? "";
                    dx.Mode = Convert.ToString(_contestData.FocusedRadioMode) ?? "";
                    double freq = Convert.ToDouble(_contestData.FocusedRadioFreq);
                    dx.FrequencyText = FormatFrequency(freq);
                    dx.FrequencyMhzText = FormatFrequencyMhz(freq);
                }
            }
            catch { }
            return dx;
        }

        private static string FormatFrequency(double dxlogFrequency)
        {
            if (dxlogFrequency <= 0) return "";
            // DXLog's FocusedRadioFreq is normally in kHz. For ON4KST skeds,
            // {FREQ} should be plain kHz, e.g. 144750, not 144.750MHz.
            double khz = dxlogFrequency;
            if (khz < 1000.0) khz = khz * 1000.0;
            return Math.Round(khz).ToString("0");
        }

        private static string FormatFrequencyMhz(double dxlogFrequency)
        {
            if (dxlogFrequency <= 0) return "";
            double mhz = dxlogFrequency >= 1000.0 ? dxlogFrequency / 1000.0 : dxlogFrequency;
            return mhz.ToString("0.###") + "MHz";
        }

        private void PutCallIntoDxLog(string call)
        {
            PutCallIntoDxLog(call, GetKstLocatorForCall(call));
        }

        private void PutCallIntoDxLog(string call, string kstLocator)
        {
            PutCallIntoDxLog(call, kstLocator, null);
        }

        private void PutCallIntoDxLog(string call, string kstLocator, Action afterInserted)
        {
            if (String.IsNullOrWhiteSpace(call)) return;
            call = CleanCall(call);
            if (String.IsNullOrWhiteSpace(call)) return;
            kstLocator = NormalizeLocator(kstLocator);

            try
            {
                FrmMain main = ResolveDxLogMainForm();
                if (main == null) throw new InvalidOperationException("DXLog main form not found");

                string queuedCall = call;
                string queuedLocator = kstLocator;
                Action queuedAfterInserted = afterInserted;

                // A context-menu Click event fires before the menu has completely
                // closed. Queue the entry-line change on DXLog's main form so the
                // menu cannot restore the old control state after this method exits.
                // Any dependent action, such as Ctrl+F12 rotator control, is queued
                // only after DXLog has accepted the callsign and locator.
                main.BeginInvoke(new MethodInvoker(delegate
                {
                    PutCallIntoDxLogOnUiThread(main, queuedCall, queuedLocator, queuedAfterInserted);
                }));
            }
            catch (Exception ex)
            {
                try { Clipboard.SetText(call); } catch { }
                UpdateStatus("DXLog insert failed, copied instead: " + ex.Message);
            }
        }

        private FrmMain ResolveDxLogMainForm()
        {
            if (_mainForm != null && !_mainForm.IsDisposed) return _mainForm;

            try
            {
                Form candidate = ParentForm == null ? Owner : ParentForm;
                _mainForm = candidate as FrmMain;
            }
            catch { }

            if (_mainForm == null || _mainForm.IsDisposed)
            {
                try
                {
                    foreach (Form form in Application.OpenForms)
                    {
                        FrmMain main = form as FrmMain;
                        if (main != null && !main.IsDisposed)
                        {
                            _mainForm = main;
                            break;
                        }
                    }
                }
                catch { }
            }

            return _mainForm;
        }

        private void PutCallIntoDxLogOnUiThread(FrmMain main, string call, string kstLocator, Action afterInserted)
        {
            try
            {
                if (main == null || main.IsDisposed) throw new InvalidOperationException("DXLog main form is unavailable");

                UCQSO qso = main.CurrentEntryLine;
                if (qso == null) throw new InvalidOperationException("No active QSO entry line");

                Control callControl = FindControlRecursive(qso, "txtCallSign");
                TextBoxBase callBox = callControl as TextBoxBase;
                if (callBox == null) throw new InvalidOperationException("txtCallSign control not found");

                callBox.Text = call;
                callBox.SelectionStart = callBox.TextLength;
                callBox.SelectionLength = 0;

                try
                {
                    main.Activate();
                    qso.Select();
                    callBox.Select();
                }
                catch { }

                // Let DXLog perform its normal callsign lookup/duplicate checks.
                InvokeDxLogKeyCommand("CHECK_CALL_CLICK", qso.Name, "txtCallSign");

                if (IsValidLocator(kstLocator))
                {
                    // DXLog's own callsign lookup can populate an older locator.
                    // Re-assert the current KST QRA after that lookup has settled,
                    // because KST is the most recent station-supplied locator.
                    System.Windows.Forms.Timer qraOverwriteTimer = new System.Windows.Forms.Timer();
                    qraOverwriteTimer.Interval = 400;
                    qraOverwriteTimer.Tick += delegate
                    {
                        qraOverwriteTimer.Stop();
                        qraOverwriteTimer.Dispose();
                        OverwriteQraFromKst(qso, call, kstLocator);
                    };
                    qraOverwriteTimer.Start();
                }

                UpdateStatus("Inserted " + call + " into DXLog" + (IsValidLocator(kstLocator) ? " / KST QRA " + kstLocator : ""));
                main.SetMainStatusText("KST selected " + call);

                if (afterInserted != null)
                {
                    // DXLog's duplicate/QRA lookup continues briefly after the
                    // callsign control is populated. Delay dependent commands so
                    // Ctrl+F12 sees the newly selected station rather than the
                    // previous log-entry contents.
                    System.Windows.Forms.Timer afterInsertTimer = new System.Windows.Forms.Timer();
                    afterInsertTimer.Interval = 650;
                    afterInsertTimer.Tick += delegate
                    {
                        afterInsertTimer.Stop();
                        afterInsertTimer.Dispose();
                        try { afterInserted(); }
                        catch (Exception callbackError)
                        {
                            UpdateStatus("Post-insert command failed: " + callbackError.Message);
                        }
                    };
                    afterInsertTimer.Start();
                }
            }
            catch (Exception ex)
            {
                try { Clipboard.SetText(call); } catch { }
                UpdateStatus("DXLog insert failed, copied instead: " + ex.Message);
            }
        }

        private static Control FindControlRecursive(Control parent, string name)
        {
            if (parent == null || String.IsNullOrWhiteSpace(name)) return null;
            if (String.Equals(parent.Name, name, StringComparison.OrdinalIgnoreCase)) return parent;

            try
            {
                foreach (Control child in parent.Controls)
                {
                    Control found = FindControlRecursive(child, name);
                    if (found != null) return found;
                }
            }
            catch { }
            return null;
        }

        private string GetKstLocatorForCall(string call)
        {
            call = CleanCall(call);
            if (String.IsNullOrWhiteSpace(call)) return "";
            KstUserInfo user;
            if (_userMap.TryGetValue(call, out user))
                return NormalizeLocator(user.Locator);
            return "";
        }

        private void OverwriteQraFromKst(UCQSO qso, string call, string kstLocator)
        {
            if (qso == null || !IsValidLocator(kstLocator)) return;

            TextBox target = GetPreferredQraTextBox(qso);
            if (target == null)
            {
                UpdateStatus("Inserted " + call + " into DXLog, but no writable Loc/Grid field was found");
                return;
            }

            string normalized = NormalizeLocator(kstLocator).ToUpperInvariant();
            target.Text = normalized;
            target.SelectionStart = target.Text.Length;
            target.SelectionLength = 0;

            // Do not leave keyboard focus in the locator field. DXLog remains the
            // active application and dependent commands (including Ctrl+F12) run
            // after this overwrite has completed.
            UpdateStatus("Inserted " + call + " into DXLog and overwrote Loc/Grid with latest KST QRA: " + normalized);
            try { if (_mainForm != null) _mainForm.SetMainStatusText("KST QRA for " + call + " = " + normalized); } catch { }
        }

        private TextBox GetPreferredQraTextBox(UCQSO qso)
        {
            // Prefer the exchange field that the current DXLog contest definition
            // declares as GRID. The KST locator is intentionally authoritative and
            // therefore replaces any older locator filled by DXLog's database.
            string gridControl = GetConfiguredGridControlName();
            if (!String.IsNullOrWhiteSpace(gridControl))
            {
                TextBox configured = FindControlRecursive(qso, gridControl) as TextBox;
                if (IsWritableQraTarget(configured)) return configured;
            }

            foreach (string name in new string[] { "txtRecInfo", "txtRecInfo2", "txtRecInfo3" })
            {
                TextBox tb = FindControlRecursive(qso, name) as TextBox;
                if (IsWritableQraTarget(tb)) return tb;
            }
            return null;
        }

        private static bool IsWritableQraTarget(TextBox tb)
        {
            return tb != null && tb.Visible && !tb.ReadOnly && tb.Enabled;
        }

        private string GetConfiguredGridControlName()
        {
            try
            {
                object activeContest = _contestData != null ? _contestData.GetType().GetField("activeContest").GetValue(_contestData) : null;
                object cdata = activeContest != null ? activeContest.GetType().GetField("cdata").GetValue(activeContest) : null;
                if (cdata == null) return "";

                if (FieldStartsWith(cdata, "field_recinfo_type", "GRID")) return "txtRecInfo";
                if (FieldStartsWith(cdata, "field_recinfo2_type", "GRID")) return "txtRecInfo2";
                if (FieldStartsWith(cdata, "field_recinfo3_type", "GRID")) return "txtRecInfo3";
            }
            catch { }
            return "";
        }

        private static bool FieldStartsWith(object obj, string fieldName, string value)
        {
            if (obj == null) return false;
            FieldInfo f = obj.GetType().GetField(fieldName);
            if (f == null) return false;
            string s = Convert.ToString(f.GetValue(obj));
            return s != null && s.StartsWith(value, StringComparison.OrdinalIgnoreCase);
        }

        private void CalculateQtfQrb(string locator, out string qtf, out string qrb)
        {
            qtf = "";
            qrb = "";
            try
            {
                string myLocator = GetOwnLocator();
                locator = NormalizeLocator(locator);
                myLocator = NormalizeLocator(myLocator);
                if (!IsValidLocator(myLocator) || !IsValidLocator(locator)) return;

                GeoPoint here = LocatorToPoint(myLocator);
                GeoPoint there = LocatorToPoint(locator);
                int bearing = (int)Math.Round(AzimuthDegrees(here, there), 0);
                if (bearing == 360) bearing = 0;
                int distance = (int)Math.Round(DistanceKm(here, there), 0);
                qtf = bearing.ToString() + "\u00B0";
                qrb = distance.ToString();
            }
            catch
            {
                qtf = "";
                qrb = "";
            }
        }

        private string GetOwnLocator()
        {
            try
            {
                if (_settings != null && !String.IsNullOrWhiteSpace(_settings.OwnLocator))
                    return _settings.OwnLocator;

                if (_contestData != null && _contestData.dalHeader != null)
                    return Convert.ToString(_contestData.dalHeader.GridSquare) ?? "";
            }
            catch { }
            return "";
        }

        private static string NormalizeLocator(string loc)
        {
            if (String.IsNullOrWhiteSpace(loc)) return "";
            loc = loc.Trim().ToUpperInvariant();
            return Regex.Replace(loc, "[^A-R0-9A-X]", "");
        }

        private static bool IsValidLocator(string loc)
        {
            if (String.IsNullOrWhiteSpace(loc)) return false;
            loc = loc.Trim().ToUpperInvariant();
            return Regex.IsMatch(loc, "^[A-R]{2}[0-9]{2}([A-X]{2})?$", RegexOptions.IgnoreCase);
        }

        private struct GeoPoint
        {
            public double Lat;
            public double Lon;
        }

        private static GeoPoint LocatorToPoint(string locator)
        {
            locator = NormalizeLocator(locator);
            double lon;
            double lat;
            if (Regex.IsMatch(locator, "^[A-R]{2}[0-9]{2}[A-X]{2}$", RegexOptions.IgnoreCase))
            {
                lon = (locator[0] - 'A') * 20.0 + (locator[2] - '0') * 2.0 + (locator[4] - 'A' + 0.5) / 12.0 - 180.0;
                lat = (locator[1] - 'A') * 10.0 + (locator[3] - '0') + (locator[5] - 'A' + 0.5) / 24.0 - 90.0;
            }
            else
            {
                lon = (locator[0] - 'A') * 20.0 + (locator[2] - '0' + 0.5) * 2.0 - 180.0;
                lat = (locator[1] - 'A') * 10.0 + (locator[3] - '0' + 0.5) - 90.0;
            }
            return new GeoPoint { Lat = lat, Lon = lon };
        }

        private static double DegToRad(double deg) { return deg * Math.PI / 180.0; }
        private static double RadToDeg(double rad) { return rad * 180.0 / Math.PI; }

        private static double DistanceKm(GeoPoint a, GeoPoint b)
        {
            double lat1 = DegToRad(a.Lat);
            double lat2 = DegToRad(b.Lat);
            double dLat = DegToRad(b.Lat - a.Lat);
            double dLon = DegToRad(b.Lon - a.Lon);
            double h = Math.Sin(dLat / 2.0) * Math.Sin(dLat / 2.0) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2.0) * Math.Sin(dLon / 2.0);
            double c = 2.0 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1.0 - h));
            return 6371.0 * c;
        }

        private static double AzimuthDegrees(GeoPoint a, GeoPoint b)
        {
            double lat1 = DegToRad(a.Lat);
            double lat2 = DegToRad(b.Lat);
            double dLon = DegToRad(b.Lon - a.Lon);
            double y = Math.Sin(dLon) * Math.Cos(lat2);
            double x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
            double bearing = (RadToDeg(Math.Atan2(y, x)) + 360.0) % 360.0;
            return bearing;
        }

        private void InvokeDxLogKeyCommand(string command, string ucName, string ctrlName)
        {
            if (_mainForm == null) return;
            MethodInfo mi = typeof(FrmMain).GetMethod("handleKeyCommand", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (mi != null) mi.Invoke(_mainForm, new object[] { command, ucName, ctrlName });
        }

        private string GetCurrentWorkedBandKey()
        {
            try
            {
                DxRadioSnapshot dx = GetDxRadioSnapshot();
                string band = (dx.Band ?? "").Trim();
                if (!String.IsNullOrWhiteSpace(band)) return band.ToUpperInvariant();
                return dx.FrequencyText ?? "";
            }
            catch { return ""; }
        }

        private void QueueWorkedCheck(string call)
        {
            call = CleanCall(call);
            if (String.IsNullOrWhiteSpace(call)) return;
            KstUserInfo user;
            if (!_userMap.TryGetValue(call, out user) || user == null) return;
            string bandKey = GetCurrentWorkedBandKey();
            if (user.WorkedCheckComplete && String.Equals(user.WorkedBandKey, bandKey, StringComparison.OrdinalIgnoreCase)) return;
            if (_workedCheckQueuedCalls.Add(call)) _workedCheckQueue.Enqueue(call);
            if (_workedCheckTimer != null && !_workedCheckTimer.Enabled) _workedCheckTimer.Start();
        }

        private void QueueWorkedChecksForAll(bool invalidateExisting)
        {
            if (invalidateExisting)
            {
                _workedCheckQueue.Clear();
                _workedCheckQueuedCalls.Clear();
                foreach (KstUserInfo user in _userMap.Values)
                {
                    user.WorkedCheckComplete = false;
                    user.WorkedBandKey = "";
                    user.WorkedCurrentBand = false;
                }
            }

            foreach (string call in _userMap.Keys) QueueWorkedCheck(call);
        }

        private void ProcessNextWorkedCheck()
        {
            if (_workedCheckQueue.Count == 0)
            {
                if (_workedCheckTimer != null) _workedCheckTimer.Stop();
                return;
            }

            string call = _workedCheckQueue.Dequeue();
            _workedCheckQueuedCalls.Remove(call);
            KstUserInfo user;
            if (!_userMap.TryGetValue(call, out user) || user == null) return;

            string bandKey = GetCurrentWorkedBandKey();
            bool oldWorked = user.WorkedCurrentBand;
            bool worked = IsWorkedBefore(call);
            user.WorkedCurrentBand = worked;
            user.WorkedBandKey = bandKey;
            user.WorkedCheckComplete = true;
            if (oldWorked != worked) UpdateMessagesForCallWorked(call, worked);

            if (_settings != null && _settings.UnworkedCurrentBandOnly)
            {
                UpsertUser(user);
                if (_workedCheckQueue.Count == 0 && _workedCheckTimer != null) _workedCheckTimer.Stop();
                return;
            }

            ListViewItem item = FindUserItem(call);
            if (item != null)
            {
                StyleUserItem(item, user);
                UpdateUserInfoCells(item, user);
            }

            if (_workedCheckQueue.Count == 0 && _workedCheckTimer != null) _workedCheckTimer.Stop();
        }

        private ListViewItem FindUserItem(string call)
        {
            if (_users == null || String.IsNullOrWhiteSpace(call)) return null;
            string clean = CleanCall(call);
            foreach (ListViewItem item in _users.Items)
                if (String.Equals(CleanCall(item.Text), clean, StringComparison.OrdinalIgnoreCase)) return item;
            return null;
        }

        private void UpdateMessagesForCallWorked(string call, bool worked)
        {
            UpdateMessagesForCallWorked(_messages, call, worked);
            UpdateMessagesForCallWorked(_threadMessages, call, worked);
        }

        private void UpdateMessagesForCallWorked(ListView list, string call, bool worked)
        {
            if (list == null || String.IsNullOrWhiteSpace(call)) return;
            string clean = CleanCall(call);
            foreach (ListViewItem item in list.Items)
            {
                KstParsedLine msg = item.Tag as KstParsedLine;
                if (msg == null) continue;
                string other = GetOtherPartyForMessage(msg);
                if (String.IsNullOrWhiteSpace(other)) other = msg.Call;
                if (!String.Equals(CleanCall(other), clean, StringComparison.OrdinalIgnoreCase)) continue;
                msg.Worked = worked;
                StyleMessageItem(item, msg, worked);
            }
            list.Invalidate();
        }

        private void UpdateWorkedFlags()
        {
            // Kept as a compatibility entry point for the existing event handlers.
            // Checks are queued and throttled rather than running a whole-room log
            // search synchronously on the DXLog UI thread.
            QueueWorkedChecksForAll(false);
        }

        private void RememberWorkedBand(object qso)
        {
            if (qso == null) return;
            try
            {
                string call = CleanCall(ReadPropertyText(qso, "Call", "Callsign", "QSOCall", "StationCallsign"));
                string band = NormalizeBandText(ReadPropertyText(qso, "Band", "BandName", "QsoBand"));
                if (String.IsNullOrWhiteSpace(call) || String.IsNullOrWhiteSpace(band)) return;

                HashSet<string> bands;
                if (!_workedBandsByCall.TryGetValue(call, out bands))
                {
                    bands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _workedBandsByCall[call] = bands;
                }
                bands.Add(band);

                KstUserInfo user;
                if (_userMap.TryGetValue(call, out user) && user != null)
                {
                    user.WorkedCurrentBand = true;
                    user.WorkedBandKey = GetCurrentWorkedBandKey();
                    user.WorkedCheckComplete = true;
                    ListViewItem item = FindUserItem(call);
                    if (item != null)
                    {
                        StyleUserItem(item, user);
                        UpdateUserInfoCells(item, user);
                    }
                }
                UpdateUserRowToolTip(call);
                if (_userSortColumn >= UserColFirstWorkedBand) SortUsers();
                RefreshConversationView();
            }
            catch { }
        }

        private void BeginBuildWorkedBandIndex()
        {
            if (_workedBandIndexBuildStarted || _contestData == null) return;
            _workedBandIndexBuildStarted = true;
            _workedBandIndexStatus = "Reading worked bands from DXLog log";

            object contestDataSnapshot = _contestData;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Dictionary<string, HashSet<string>> result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                string status;
                try
                {
                    HashSet<object> visited = new HashSet<object>(ReferenceObjectComparer.Instance);
                    CollectQsoBandsFromObject(contestDataSnapshot, result, 3, visited);
                    status = result.Count > 0
                        ? "Worked-band index loaded for " + result.Count.ToString() + " calls"
                        : "DXLog worked-band collection was not exposed; tracking new QSOs only";
                }
                catch (Exception ex)
                {
                    status = "Worked-band index unavailable: " + ex.Message;
                }

                SafeUi(delegate
                {
                    foreach (KeyValuePair<string, HashSet<string>> kv in result)
                    {
                        HashSet<string> target;
                        if (!_workedBandsByCall.TryGetValue(kv.Key, out target))
                        {
                            target = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            _workedBandsByCall[kv.Key] = target;
                        }
                        foreach (string band in kv.Value) target.Add(band);
                    }
                    _workedBandIndexComplete = result.Count > 0;
                    _workedBandIndexStatus = status;
                    UpdateAllUserInfoCells();
                    if (_userSortColumn >= UserColFirstWorkedBand) SortUsers();
                    RefreshConversationView();
                });
            });
        }

        private static void CollectQsoBandsFromObject(object value, Dictionary<string, HashSet<string>> result, int depth, HashSet<object> visited)
        {
            if (value == null || depth < 0 || result == null || visited == null) return;
            Type type = value.GetType();
            if (IsSimpleReflectionType(type)) return;
            if (!visited.Add(value)) return;

            IEnumerable enumerable = value as IEnumerable;
            if (enumerable != null)
            {
                int count = 0;
                try
                {
                    foreach (object item in enumerable)
                    {
                        if (item == null) continue;
                        AddQsoBandFromObject(item, result);
                        if (depth > 0 && String.IsNullOrWhiteSpace(ReadPropertyText(item, "Call", "Callsign", "QSOCall", "StationCallsign")))
                            CollectQsoBandsFromObject(item, result, depth - 1, visited);
                        if (++count >= 200000) break;
                    }
                }
                catch { }
                return;
            }

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (PropertyInfo property in type.GetProperties(flags))
            {
                if (property.GetIndexParameters().Length != 0 || !LooksLikeLogMember(property.Name)) continue;
                try
                {
                    object child = property.GetValue(value, null);
                    CollectQsoBandsFromObject(child, result, depth - 1, visited);
                }
                catch { }
            }
            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (!LooksLikeLogMember(field.Name)) continue;
                try
                {
                    object child = field.GetValue(value);
                    CollectQsoBandsFromObject(child, result, depth - 1, visited);
                }
                catch { }
            }
        }

        private static void AddQsoBandFromObject(object qso, Dictionary<string, HashSet<string>> result)
        {
            string call = CleanCall(ReadPropertyText(qso, "Call", "Callsign", "QSOCall", "StationCallsign"));
            string band = NormalizeBandText(ReadPropertyText(qso, "Band", "BandName", "QsoBand"));
            if (String.IsNullOrWhiteSpace(call) || String.IsNullOrWhiteSpace(band)) return;
            HashSet<string> bands;
            if (!result.TryGetValue(call, out bands))
            {
                bands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                result[call] = bands;
            }
            bands.Add(band);
        }

        private static bool LooksLikeLogMember(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) return false;
            string n = name.ToLowerInvariant();
            return n.Contains("qso") || n.Contains("log") || n.Contains("contact");
        }

        private static bool IsSimpleReflectionType(Type type)
        {
            if (type == null) return true;
            return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
                   type == typeof(DateTime) || type == typeof(TimeSpan) || type == typeof(Guid);
        }

        private static string NormalizeBandText(string band)
        {
            return KstWorkedBands.NormalizeKey(band);
        }

        private void UpdateAllUserToolTips()
        {
            UpdateAllUserInfoCells();
        }

        private void UpdateAllUserInfoCells()
        {
            if (_users == null) return;
            foreach (ListViewItem item in _users.Items) UpdateUserInfoCells(item, item.Tag as KstUserInfo);
        }

        private static string ReadPropertyText(object value, params string[] names)
        {
            if (value == null || names == null) return "";
            Type t = value.GetType();
            foreach (string name in names)
            {
                try
                {
                    PropertyInfo pi = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                    if (pi != null)
                    {
                        object v = pi.GetValue(value, null);
                        if (v != null) return Convert.ToString(v);
                    }
                    FieldInfo fi = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                    if (fi != null)
                    {
                        object v = fi.GetValue(value);
                        if (v != null) return Convert.ToString(v);
                    }
                }
                catch { }
            }
            return "";
        }

        private bool IsWorkedBefore(string call)
        {
            if (_contestData == null || _mainForm == null || String.IsNullOrWhiteSpace(call)) return false;
            call = CleanCall(call);
            if (String.IsNullOrWhiteSpace(call)) return false;
            try
            {
                UCQSO qso = _mainForm.CurrentEntryLine;
                if (qso == null || qso.ActualQSO == null) return false;
                return _contestData.CheckDoubleQSO(
                    qso.ActualQSO.IDQSO,
                    call,
                    qso.ActualQSO.Period,
                    qso.ActualQSO.Band,
                    qso.ActualQSO.Mode,
                    qso.ActualQSO.Rcvd4,
                    qso.ActualQSO.RecInfo,
                    qso.ActualQSO.QSOTime,
                    true) != 0;
            }
            catch { return false; }
        }

        private void SubscribeDxLogQsoSavedEvent()
        {
            try
            {
                if (_mainForm == null || _subscribedNewQsoSaved) return;
                _mainForm.NewQSOSaved += HandleDxLogNewQsoSaved;
                _subscribedNewQsoSaved = true;
            }
            catch { }
        }

        private void UnsubscribeDxLogQsoSavedEvent()
        {
            try
            {
                if (_mainForm == null || !_subscribedNewQsoSaved) return;
                _mainForm.NewQSOSaved -= HandleDxLogNewQsoSaved;
                _subscribedNewQsoSaved = false;
            }
            catch { }
        }

        private void HandleDxLogNewQsoSaved(DXQSO newQso)
        {
            SafeUi(delegate
            {
                try
                {
                    // Re-check all visible calls against the DXLog log immediately,
                    // then request a fresh ON4KST user list so the user/map panes
                    // reflect the latest worked status without waiting 10 seconds.
                    RememberWorkedBand(newQso);
                    UpdateWorkedFlags();
                    RecheckMessageWorkedState();
                    RefreshConversationView();
                    RefreshMapWindow();

                    if (_qsoLoggedRefreshTimer != null)
                    {
                        _qsoLoggedRefreshTimer.Stop();
                        _qsoLoggedRefreshTimer.Start();
                    }
                    else
                    {
                        _ = ForceRefreshAfterQsoLoggedAsync();
                    }
                }
                catch { }
            });
        }

        private async Task ForceRefreshAfterQsoLoggedAsync()
        {
            try
            {
                RecheckMessageWorkedState();
                UpdateWorkedFlags();
                RestyleMessages();
                RestyleThreadMessages();
                RefreshConversationView();
                RefreshMapWindow();

                if (_kst != null && _kst.IsConnected)
                {
                    UpdateStatus("QSO logged - refreshing KST user list");
                    await RefreshUsers();
                }
                else
                {
                    UpdateStatus("QSO logged - KST not connected");
                }
            }
            catch { }
        }

        private void RecheckMessageWorkedState()
        {
            try
            {
                UpdateMessageWorkedStateFromCache(_messages);
                UpdateMessageWorkedStateFromCache(_threadMessages);
            }
            catch { }
        }

        private void UpdateMessageWorkedStateFromCache(ListView list)
        {
            if (list == null) return;
            foreach (ListViewItem item in list.Items)
            {
                KstParsedLine msg = item.Tag as KstParsedLine;
                if (msg == null) continue;
                string call = GetOtherPartyForMessage(msg);
                if (String.IsNullOrWhiteSpace(call)) call = msg.Call;
                KstUserInfo user;
                msg.Worked = _userMap.TryGetValue(CleanCall(call), out user) && user != null && user.WorkedCurrentBand;
            }
        }

        private void HandleDxLogFocusChanged()
        {
            SafeUi(delegate
            {
                UpdateStatus("DXLog radio " + (_contestData != null ? _contestData.FocusedRadio.ToString() : "?") + " | KST " + KstRoomTitles.GetTitle(_settings.Room) + " " + (_kst != null && _kst.IsConnected ? "connected" : "not connected"));
                QueueWorkedChecksForAll(true);
                _airScoutResults.Clear();
                RefreshAllAirScoutCells();
                ResetAirScoutAutoScan(true);
                QuerySelectedUserInAirScout(true);
                ForceBandChangeUserAndMapRefresh();
            });
        }

        private void UsersSelectedIndexChanged()
        {
            string previous = _lastStyledUserCall;
            string current = "";
            if (_users != null && _users.SelectedItems.Count > 0)
            {
                current = _users.SelectedItems[0].Text;
                _lastSelectedCall = current;
                MarkCallRead(current);
                QuerySelectedUserInAirScout(true);
            }
            _lastStyledUserCall = current;
            RestyleUserCall(previous);
            RestyleUserCall(current);
            UpdateComposeTarget();
            RefreshConversationView();
            RefreshMapWindow();
        }

        private void ConfigureAirScoutClient()
        {
            try
            {
                if (_airScoutRefreshTimer != null) _airScoutRefreshTimer.Stop();
                if (_airScout != null)
                {
                    _airScout.PathResultReceived -= AirScoutPathResultReceived;
                    _airScout.StatusChanged -= AirScoutStatusChanged;
                    _airScout.Dispose();
                    _airScout = null;
                }

                _lastAirScoutReplyUtc = DateTime.MinValue;
                _lastAirScoutQueryUtc = DateTime.MinValue;
                _lastAirScoutQueryCall = "";
                _lastAirScoutQueryQrg = 0;
                ResetAirScoutAutoScan(true);
                _airScoutResults.Clear();
                lock (_airScoutPlaneLock)
                {
                    _airScoutPlaneById.Clear();
                    _lastAirScoutPlaneFetchUtc = DateTime.MinValue;
                    _lastGoodAirScoutPlaneFetchUtc = DateTime.MinValue;
                    _airScoutPlaneFeedStatus = "Aircraft not read";
                }
                RefreshAllAirScoutCells();
                if (_settings == null || !_settings.AirScoutEnabled)
                {
                    UpdateAirScoutStatusLabel();
                    RefreshAllAirScoutCells();
                    return;
                }

                _airScout = new AirScoutClient(_settings.AirScoutPort, "KST", "AS");
                _airScout.PathResultReceived += AirScoutPathResultReceived;
                _airScout.StatusChanged += AirScoutStatusChanged;
                _airScout.Start();
                if (_airScoutRefreshTimer != null) _airScoutRefreshTimer.Start();
                ResetAirScoutAutoScan(true);
                UpdateAirScoutStatusLabel();
                QuerySelectedUserInAirScout(true);
            }
            catch (Exception ex)
            {
                if (_airScoutStatusLabel != null) _airScoutStatusLabel.Text = "AirScout: Error";
                UpdateStatus("AirScout setup failed: " + ex.Message);
            }
        }

        private void AirScoutStatusChanged(string status)
        {
            SafeUi(delegate
            {
                if (!String.IsNullOrWhiteSpace(status) && status.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
                {
                    if (_airScoutStatusLabel != null) _airScoutStatusLabel.Text = "AirScout: Error";
                    UpdateStatus("AirScout " + status);
                }
                else
                {
                    UpdateAirScoutStatusLabel();
                }
            });
        }

        private void AirScoutPathResultReceived(AirScoutPathResult result)
        {
            if (result == null || String.IsNullOrWhiteSpace(result.DxCall)) return;
            SafeUi(delegate
            {
                string call = CleanCall(result.DxCall);
                if (String.IsNullOrWhiteSpace(call)) return;
                AirScoutPathResult previousResult;
                _airScoutResults.TryGetValue(call, out previousResult);
                _airScoutResults[call] = result;
                _lastAirScoutReplyUtc = DateTime.UtcNow;
                if (_lastAirScoutQueryUtc != DateTime.MinValue)
                    _perfLastAirScoutReplyMs = Math.Max(0, (long)(DateTime.UtcNow - _lastAirScoutQueryUtc).TotalMilliseconds);
                if (String.Equals(_airScoutPendingAutoCall, call, StringComparison.OrdinalIgnoreCase))
                {
                    _airScoutPendingAutoCall = "";
                    _airScoutPendingAutoSinceUtc = DateTime.MinValue;
                    _airScoutScanCompleted++;
                    if (_airScoutScanCompleted > _airScoutScanTotal) _airScoutScanCompleted = _airScoutScanTotal;
                    if (_airScoutScanQueue.Count == 0 && !_airScoutRescanRequested)
                        _lastAirScoutFullScanUtc = DateTime.UtcNow;
                }
                if (_settings != null && (_settings.AirScoutFilterMinutes >= 0 || _settings.AircraftFilterMode != 0))
                {
                    KstUserInfo changedUser;
                    if (_userMap.TryGetValue(call, out changedUser) && changedUser != null) UpsertUser(changedUser);
                }
                else
                    UpdateAirScoutRow(call);
                if (_settings != null && _settings.AircraftFilterMode >= (int)KstAircraftFilterMode.PassengerOrCargo)
                    BeginRefreshAirScoutLivePlanes(false);
                MaybeRaiseAirScoutAlert(call, previousResult, result);
                if (_settings != null && _settings.AirScoutAutoSort) SortUsersByAirScoutOpportunity();
                UpdateAirScoutStatusLabel();
                if (String.Equals(call, CleanCall(_lastSelectedCall), StringComparison.OrdinalIgnoreCase)) RefreshMapAircraftOnly();
            });
        }

        private void MaybeRaiseAirScoutAlert(string call, AirScoutPathResult previous, AirScoutPathResult current)
        {
            if (_settings == null || !_settings.AirScoutAlertsEnabled || current == null) return;
            AirScoutPlaneInfo best = GetBestFilteredAirScoutPlane(current);
            if (best == null || best.Mins > _settings.AirScoutAlertMinutes) return;
            AirScoutPlaneInfo oldBest = previous == null ? null : GetBestFilteredAirScoutPlane(previous);
            bool crossedThreshold = oldBest == null || oldBest.Mins > _settings.AirScoutAlertMinutes;
            bool becameNow = best.Mins <= 0 && (oldBest == null || oldBest.Mins > 0);
            if (!crossedThreshold && !becameNow) return;

            DateTime last;
            if (_lastAirScoutAlertUtcByCall.TryGetValue(call, out last) && DateTime.UtcNow - last < TimeSpan.FromMinutes(2) && !becameNow) return;
            _lastAirScoutAlertUtcByCall[call] = DateTime.UtcNow;
            string when = best.Mins <= 0 ? "NOW" : "in " + best.Mins.ToString() + " min";
            string text = call + " aircraft opportunity " + when;
            if (!String.IsNullOrWhiteSpace(best.Call)) text += " (" + best.Call.Trim() + ")";
            UpdateStatus(text);
            try { SystemSounds.Asterisk.Play(); } catch { }
            try
            {
                if (_airScoutAlertToolTip != null && _airScoutStatusLabel != null)
                    _airScoutAlertToolTip.Show(text, _airScoutStatusLabel, 0, -35, 6000);
            }
            catch { }
        }

        private void UpdateAirScoutStatusLabel()
        {
            if (_airScoutStatusLabel == null) return;
            if (_settings == null || !_settings.AirScoutEnabled)
            {
                _airScoutStatusLabel.Text = "AirScout: Off";
                return;
            }
            if (_airScout == null || !_airScout.IsListening)
            {
                _airScoutStatusLabel.Text = "AirScout: Error";
                return;
            }
            if (_mapForm != null && !_mapForm.IsDisposed && _mapForm.Visible && _lastGoodAirScoutPlaneFetchUtc != DateTime.MinValue)
            {
                int age = Math.Max(0, (int)(DateTime.UtcNow - _lastGoodAirScoutPlaneFetchUtc).TotalSeconds);
                if (age > 45)
                {
                    _airScoutStatusLabel.Text = "AirScout: feed stale " + age.ToString() + "s";
                    return;
                }
            }
            if (_lastAirScoutReplyUtc != DateTime.MinValue && DateTime.UtcNow - _lastAirScoutReplyUtc < TimeSpan.FromSeconds(60))
            {
                if (_airScoutScanTotal > 0 && (_airScoutScanQueue.Count > 0 || !String.IsNullOrWhiteSpace(_airScoutPendingAutoCall)))
                    _airScoutStatusLabel.Text = "AirScout: OK  " + Math.Min(_airScoutScanCompleted, _airScoutScanTotal).ToString() + "/" + _airScoutScanTotal.ToString();
                else
                    _airScoutStatusLabel.Text = "AirScout: OK";
                return;
            }
            if (_lastAirScoutQueryUtc != DateTime.MinValue)
            {
                string call = String.IsNullOrWhiteSpace(_lastAirScoutQueryCall) ? "" : " " + _lastAirScoutQueryCall;
                _airScoutStatusLabel.Text = "AirScout: Waiting" + call;
                return;
            }
            _airScoutStatusLabel.Text = "AirScout: Listening";
        }

        private void ResetAirScoutAutoScan(bool startImmediately)
        {
            _airScoutScanQueue.Clear();
            _airScoutScanQueuedCalls.Clear();
            _airScoutPendingAutoCall = "";
            _airScoutPendingAutoSinceUtc = DateTime.MinValue;
            _airScoutScanTotal = 0;
            _airScoutScanCompleted = 0;
            _airScoutAutoScanQrg = GetAirScoutFrequency100Hz();
            _airScoutRescanRequested = false;
            _lastAirScoutFullScanUtc = startImmediately ? DateTime.MinValue : DateTime.UtcNow;
        }

        private void RequestAirScoutRescan(bool startImmediately)
        {
            bool passInProgress = !String.IsNullOrWhiteSpace(_airScoutPendingAutoCall) || _airScoutScanQueue.Count > 0;
            if (passInProgress)
            {
                _airScoutRescanRequested = true;
                if (startImmediately) _lastAirScoutFullScanUtc = DateTime.MinValue;
                return;
            }

            ResetAirScoutAutoScan(startImmediately);
        }

        private void RequestAirScoutRescanForLatestUsers()
        {
            string signature = GetAirScoutUserSnapshotSignature();
            if (String.Equals(signature, _airScoutUserSnapshotSignature, StringComparison.Ordinal)) return;
            _airScoutUserSnapshotSignature = signature;
            RequestAirScoutRescan(true);
        }

        private string GetAirScoutUserSnapshotSignature()
        {
            List<string> entries = new List<string>();
            foreach (KstUserInfo user in _userMap.Values)
            {
                if (user == null || !IsUserWithinDistanceFilter(user)) continue;
                string call = CleanCall(user.Call);
                string locator = NormalizeLocator(user.Locator);
                if (String.IsNullOrWhiteSpace(call) || !IsValidLocator(locator)) continue;
                entries.Add((IsWatchedCall(call) ? "0|" : "1|") + call + "|" + locator);
            }
            entries.Sort(StringComparer.OrdinalIgnoreCase);
            return String.Join(";", entries.ToArray());
        }

        private void BuildAirScoutAutoScanQueue()
        {
            _airScoutScanQueue.Clear();
            _airScoutScanQueuedCalls.Clear();
            _airScoutScanCompleted = 0;
            _airScoutScanTotal = 0;
            _airScoutAutoScanQrg = GetAirScoutFrequency100Hz();
            _airScoutRescanRequested = false;

            if (_settings == null || !_settings.AirScoutEnabled) return;

            string myCall = CleanCall(_settings.Callsign);
            List<string> watched = new List<string>();
            List<string> normal = new List<string>();
            foreach (KstUserInfo user in _userMap.Values)
            {
                if (user == null || !IsUserWithinDistanceFilter(user)) continue;
                string call = CleanCall(user.Call);
                if (String.IsNullOrWhiteSpace(call) || String.Equals(call, myCall, StringComparison.OrdinalIgnoreCase)) continue;
                if (!CanQueryAirScoutForCall(call)) continue;
                if (IsWatchedCall(call)) watched.Add(call); else normal.Add(call);
            }
            watched.Sort(StringComparer.OrdinalIgnoreCase);
            normal.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string call in watched) if (_airScoutScanQueuedCalls.Add(call)) _airScoutScanQueue.Enqueue(call);
            foreach (string call in normal) if (_airScoutScanQueuedCalls.Add(call)) _airScoutScanQueue.Enqueue(call);
            _airScoutScanTotal = _airScoutScanQueue.Count;
            _airScoutUserSnapshotSignature = GetAirScoutUserSnapshotSignature();
        }

        private void RunAirScoutAutoScanTick()
        {
            if (_settings == null || !_settings.AirScoutEnabled || _airScout == null || !_airScout.IsListening) return;

            long qrg = GetAirScoutFrequency100Hz();
            if (qrg <= 0) return;
            if (_airScoutAutoScanQrg != 0 && qrg != _airScoutAutoScanQrg)
            {
                // AirScout results and DXLog worked state are band-specific.
                // Start a clean scan and force both the KST user snapshot and map
                // to refresh immediately when the operating band changes.
                _airScoutResults.Clear();
                RefreshAllAirScoutCells();
                ResetAirScoutAutoScan(true);
                QueueWorkedChecksForAll(true);
                QuerySelectedUserInAirScout(true);
                ForceBandChangeUserAndMapRefresh();
            }

            if (!String.IsNullOrWhiteSpace(_airScoutPendingAutoCall))
            {
                // Wait for the reply belonging to the current queue item. If none
                // arrives, skip it after two seconds and continue the same pass.
                if (DateTime.UtcNow - _airScoutPendingAutoSinceUtc < TimeSpan.FromSeconds(2)) return;
                _airScoutPendingAutoCall = "";
                _airScoutPendingAutoSinceUtc = DateTime.MinValue;
                _airScoutScanCompleted++;
                if (_airScoutScanCompleted > _airScoutScanTotal) _airScoutScanCompleted = _airScoutScanTotal;
            }

            if (_airScoutScanQueue.Count == 0)
            {
                bool completedPass = _airScoutScanTotal > 0 && _airScoutScanCompleted >= _airScoutScanTotal;
                if (completedPass)
                {
                    if (_airScoutRescanRequested)
                    {
                        // A KST refresh changed the visible user set while this pass
                        // was running. Accept the new snapshot only now, after all
                        // entries in the old pass have completed or timed out.
                        BuildAirScoutAutoScanQueue();
                    }
                    else
                    {
                        if (_lastAirScoutFullScanUtc == DateTime.MinValue) _lastAirScoutFullScanUtc = DateTime.UtcNow;
                        if (DateTime.UtcNow - _lastAirScoutFullScanUtc < TimeSpan.FromSeconds(20)) return;
                        BuildAirScoutAutoScanQueue();
                    }
                }
                else
                {
                    BuildAirScoutAutoScanQueue();
                }

                if (_airScoutScanQueue.Count == 0) return;
                _lastAirScoutFullScanUtc = DateTime.MinValue;
            }

            string callToQuery = _airScoutScanQueue.Dequeue();
            _airScoutScanQueuedCalls.Remove(callToQuery);
            if (!CanQueryAirScoutForCall(callToQuery))
            {
                _airScoutScanCompleted++;
                return;
            }

            _airScoutPendingAutoCall = callToQuery;
            _airScoutPendingAutoSinceUtc = DateTime.UtcNow;
            QueryCallInAirScout(callToQuery, false, false);
        }

        private void ForceBandChangeUserAndMapRefresh()
        {
            RefreshMapWindow();

            if (_kst == null || !_kst.IsConnected) return;
            if (_refreshingUserList)
            {
                _forceUserRefreshAfterCurrent = true;
                return;
            }

            _ = RefreshUsersForBandChangeAsync();
        }

        private async Task RefreshUsersForBandChangeAsync()
        {
            if (_bandChangeRefreshRunning) return;
            _bandChangeRefreshRunning = true;
            try
            {
                if (_kst == null || !_kst.IsConnected) return;
                if (_refreshingUserList)
                {
                    _forceUserRefreshAfterCurrent = true;
                    return;
                }

                UpdateStatus("Band changed - refreshing KST users and map; macros: " + GetMacroProfileTitle(GetActiveMacroProfileKey()));
                for (int i = 0; i < 4; i++) UpdateMacroToolTip(i);
                RebuildVisibleUserList();
                RefreshMapWindow();
                await RefreshUsers();
            }
            catch { }
            finally
            {
                _bandChangeRefreshRunning = false;
            }
        }

        private bool CanQueryAirScoutForCall(string call)
        {
            if (_settings == null || !_settings.AirScoutEnabled || _airScout == null || !_airScout.IsListening) return false;
            call = CleanCall(call);
            if (String.IsNullOrWhiteSpace(call)) return false;
            string myCall = CleanCall(_settings.Callsign);
            string myLoc = NormalizeLocator(GetOwnLocator());
            string dxLoc = NormalizeLocator(GetKstLocatorForCall(call));
            return !String.IsNullOrWhiteSpace(myCall) && IsValidLocator(myLoc) && IsValidLocator(dxLoc) && GetAirScoutFrequency100Hz() > 0;
        }

        private void QuerySelectedUserInAirScout(bool showValidationStatus)
        {
            if (_users == null || _users.SelectedItems.Count == 0) return;
            QueryCallInAirScout(_users.SelectedItems[0].Text, false, showValidationStatus);
        }

        private void ShowCallPathInAirScout(string call)
        {
            QueryCallInAirScout(call, true, true);
        }

        private void QueryCallInAirScout(string call, bool showPath, bool showValidationStatus)
        {
            try
            {
                if (_settings == null || !_settings.AirScoutEnabled || _airScout == null || !_airScout.IsListening)
                {
                    if (showValidationStatus) UpdateStatus("AirScout is not enabled or its UDP listener is not available");
                    return;
                }

                call = CleanCall(call);
                string myCall = CleanCall(_settings.Callsign);
                string myLoc = NormalizeLocator(GetOwnLocator());
                string dxLoc = NormalizeLocator(GetKstLocatorForCall(call));
                long qrg = GetAirScoutFrequency100Hz();

                if (String.IsNullOrWhiteSpace(myCall))
                {
                    if (showValidationStatus) UpdateStatus("AirScout needs your callsign in Setup");
                    return;
                }
                if (!IsValidLocator(myLoc))
                {
                    if (showValidationStatus) UpdateStatus("AirScout needs a valid own QTH locator in Setup");
                    return;
                }
                if (!IsValidLocator(dxLoc))
                {
                    if (showValidationStatus) UpdateStatus("No valid KST locator for " + call + " - cannot set AirScout path");
                    return;
                }
                if (qrg <= 0)
                {
                    if (showValidationStatus) UpdateStatus("No valid DXLog radio frequency - cannot set AirScout path");
                    return;
                }

                string data = qrg.ToString() + "," + myCall + "," + myLoc + "," + call + "," + dxLoc;
                _lastAirScoutQueryUtc = DateTime.UtcNow;
                _lastAirScoutQueryCall = call;
                _lastAirScoutQueryQrg = qrg;
                _airScout.SendSetPath(data);
                UpdateAirScoutStatusLabel();
                if (showValidationStatus)
                    UpdateStatus("AirScout TX: " + call + " " + dxLoc + " @ " + qrg.ToString() + " (100 Hz units)");
                if (showPath)
                {
                    _airScout.SendShowPath(data);
                    UpdateStatus("AirScout path shown: " + myCall + " " + myLoc + " to " + call + " " + dxLoc);
                }
            }
            catch (Exception ex)
            {
                if (showValidationStatus) UpdateStatus("AirScout query failed: " + ex.Message);
            }
        }

        private long GetAirScoutFrequency100Hz()
        {
            DxRadioSnapshot dx = GetDxRadioSnapshot();
            long khz;
            if (!Int64.TryParse(dx.FrequencyText, out khz) || khz <= 0) return 0;

            // AirScout / wtKST use canonical band frequencies in 100 Hz units
            // (for example 1440000 for 144 MHz), rather than the exact VFO.
            // Mapping the live DXLog frequency to its amateur band also avoids
            // dummy/test VFO values such as 142000 kHz being rejected by AirScout.
            if (khz >= 50000 && khz < 54000) return 500000L;
            if (khz >= 70000 && khz < 71000) return 700000L;
            if (khz >= 140000 && khz < 150000) return 1440000L;
            if (khz >= 420000 && khz < 450000) return 4320000L;
            if (khz >= 1240000 && khz < 1320000) return 12960000L;
            if (khz >= 2300000 && khz < 2450000) return 23200000L;
            if (khz >= 3300000 && khz < 3500000) return 34000000L;
            if (khz >= 5650000 && khz < 5850000) return 57600000L;
            if (khz >= 10000000 && khz < 10500000) return 103680000L;

            // Fallback for other frequencies: preserve the original exact-VFO behavior.
            return khz * 10L;
        }

        private string GetAirScoutDisplay(string call)
        {
            if (_settings == null || !_settings.AirScoutEnabled) return "";
            AirScoutPathResult result;
            if (!_airScoutResults.TryGetValue(CleanCall(call), out result) || result == null) return "";
            AirScoutPlaneInfo best = GetBestFilteredAirScoutPlane(result);
            if (best == null) return "-";
            return best.Mins <= 0 ? "NOW" : best.Mins.ToString() + "m";
        }

        private void RefreshAllAirScoutCells()
        {
            if (_users == null) return;
            foreach (ListViewItem item in _users.Items)
            {
                EnsureUserSubItems(item);
                item.SubItems[UserColAirScout].Text = GetAirScoutDisplay(item.Text);
                UpdateAirScoutItemToolTip(item);
            }
        }

        private void UpdateAirScoutRow(string call)
        {
            if (_users == null) return;
            foreach (ListViewItem item in _users.Items)
            {
                if (!String.Equals(CleanCall(item.Text), CleanCall(call), StringComparison.OrdinalIgnoreCase)) continue;
                EnsureUserSubItems(item);
                item.SubItems[UserColAirScout].Text = GetAirScoutDisplay(call);
                UpdateAirScoutItemToolTip(item);
                break;
            }
        }

        private void UpdateUserRowToolTip(string call)
        {
            UpdateUserInfoCells(call);
        }

        private void UpdateUserInfoCells(string call)
        {
            if (_users == null || String.IsNullOrWhiteSpace(call)) return;
            ListViewItem item = FindUserItem(call);
            if (item == null) return;
            UpdateUserInfoCells(item, item.Tag as KstUserInfo);
        }

        private void UpdateUserInfoCells(ListViewItem item, KstUserInfo user)
        {
            if (item == null) return;
            EnsureUserSubItems(item);
            item.SubItems[UserColActive].Text = GetLastActiveDisplay(user);
            item.SubItems[UserColUnread].Text = GetUnreadDisplay(item.Text);
            List<KstWorkedBandOption> visibleBands = GetVisibleWorkedBandOptions();
            for (int i = 0; i < visibleBands.Count; i++)
                item.SubItems[UserColFirstWorkedBand + i].Text = GetWorkedBandCell(item.Text, visibleBands[i].Key);
            UpdateAirScoutItemToolTip(item);
            try { if (_users != null) _users.Invalidate(item.Bounds); } catch { }
        }

        private string GetUnreadDisplay(string call)
        {
            int count;
            return _unreadDirectedByCall.TryGetValue(CleanCall(call), out count) && count > 0 ? count.ToString() : "";
        }

        private void MarkCallRead(string call)
        {
            call = CleanCall(call);
            if (String.IsNullOrWhiteSpace(call)) return;
            if (_unreadDirectedByCall.Remove(call)) UpdateUserInfoCells(call);
        }

        private static string GetLastActiveDisplay(KstUserInfo user)
        {
            return user == null || user.LastActivityUtc == DateTime.MinValue
                ? "--"
                : user.LastActivityUtc.ToString("HHmm");
        }

        private string GetWorkedBandsDisplay(string call)
        {
            HashSet<string> bands;
            if (!_workedBandsByCall.TryGetValue(CleanCall(call), out bands) || bands == null || bands.Count == 0)
                return "--";

            List<string> sorted = new List<string>(bands);
            sorted.Sort(CompareBandLabels);
            return String.Join(",", sorted.ToArray());
        }

        private static int CompareBandLabels(string a, string b)
        {
            double av = BandSortValue(a);
            double bv = BandSortValue(b);
            int result = av.CompareTo(bv);
            return result != 0 ? result : String.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static double BandSortValue(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return Double.MaxValue;
            string text = value.Trim().ToLowerInvariant().Replace("mhz", "").Replace("ghz", "");
            double number;
            if (!Double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number))
                return Double.MaxValue;
            // DXLog normally supplies MHz. Decimal values below 20 are commonly GHz.
            return number > 0 && number < 20 ? number * 1000.0 : number;
        }

        private void UpdateAirScoutItemToolTip(ListViewItem item)
        {
            if (item == null) return;
            StringBuilder tip = new StringBuilder();
            KstUserInfo user = item.Tag as KstUserInfo;
            string call = CleanCall(item.Text);
            tip.Append(IsWatchedCall(call) ? "★ " : "").Append(call);
            if (user != null)
            {
                if (!String.IsNullOrWhiteSpace(user.Name)) tip.Append("  ").Append(user.Name);
                if (!String.IsNullOrWhiteSpace(user.Locator)) tip.AppendLine().Append("Locator: ").Append(user.Locator);
                if (user.LastActivityUtc != DateTime.MinValue)
                    tip.AppendLine().Append("Last KST message seen this session: ").Append(user.LastActivityUtc.ToString("HH:mm:ss")).Append(" UTC");
                else
                    tip.AppendLine().Append("Last KST message seen this session: none");
                tip.AppendLine().Append("Worked on current DXLog band: ").Append(user.WorkedCurrentBand ? "Yes" : "No");
            }
            int unread;
            if (_unreadDirectedByCall.TryGetValue(call, out unread) && unread > 0)
                tip.AppendLine().Append("Unread directed messages: ").Append(unread.ToString());
            string stationNote = GetStationNote(call);
            if (!String.IsNullOrWhiteSpace(stationNote)) tip.AppendLine().Append("Note: ").Append(stationNote);
            HashSet<string> bands;
            if (_workedBandsByCall.TryGetValue(call, out bands) && bands.Count > 0)
            {
                List<string> sortedBands = new List<string>(bands);
                sortedBands.Sort(StringComparer.OrdinalIgnoreCase);
                tip.AppendLine().Append("Worked bands: ").Append(String.Join(", ", sortedBands.ToArray()));
            }

            AirScoutPathResult result;
            if (_airScoutResults.TryGetValue(call, out result) && result != null)
            {
                AirScoutPlaneInfo best = GetBestFilteredAirScoutPlane(result);
                if (best == null)
                {
                    tip.AppendLine().Append("AirScout: no suitable aircraft reported");
                }
                else
                {
                    string opportunity = best.Mins <= 0 ? "now" : "in " + best.Mins.ToString() + " min";
                    tip.AppendLine().Append("AirScout aircraft: ").Append(best.Call);
                    string sizeText = GetAirScoutSizeText(best);
                    if (!String.IsNullOrWhiteSpace(sizeText)) tip.AppendLine().Append("Size: ").Append(sizeText);
                    AirScoutLivePlane livePlane;
                    if (TryGetLivePlaneForAirScoutCandidate(best.Call, out livePlane) && livePlane != null)
                    {
                        if (!String.IsNullOrWhiteSpace(livePlane.Flight) && !String.Equals(NormalizeAircraftId(livePlane.Flight), NormalizeAircraftId(best.Call), StringComparison.OrdinalIgnoreCase))
                            tip.AppendLine().Append("Flight: ").Append(livePlane.Flight.Trim());
                        if (!String.IsNullOrWhiteSpace(livePlane.Registration))
                            tip.AppendLine().Append("Registration: ").Append(livePlane.Registration.Trim());
                        string model = GetAircraftModelName(livePlane.Type);
                        if (!String.IsNullOrWhiteSpace(model))
                            tip.AppendLine().Append("Aircraft: ").Append(model);
                        if (!String.IsNullOrWhiteSpace(livePlane.Type))
                            tip.AppendLine().Append("Type: ").Append(livePlane.Type.Trim());
                        tip.AppendLine().Append("Category: ").Append(GetAircraftTrafficClassText(ClassifyAircraftTraffic(livePlane, best))).Append(" (classified)");
                    }
                    else if (_settings != null && (_settings.AircraftFilterMode == (int)KstAircraftFilterMode.PassengerOrCargo || _settings.AircraftFilterMode == (int)KstAircraftFilterMode.CargoOnly))
                    {
                        tip.AppendLine().Append("Category: unavailable until this aircraft appears in AirScout /planes.json");
                    }
                    tip.AppendLine().Append("Opportunity: ").Append(opportunity);
                    tip.AppendLine().Append("Potential: ").Append(best.Potential.ToString());
                    tip.AppendLine().Append("Intersection QRB: ").Append(best.IntQRB.ToString()).Append(" km");
                    if (livePlane != null)
                    {
                        if (livePlane.AltitudeFt > 0) tip.AppendLine().Append("Altitude: ").Append(livePlane.AltitudeFt.ToString("N0")).Append(" ft");
                        if (livePlane.SpeedKt > 0) tip.AppendLine().Append("Speed: ").Append(livePlane.SpeedKt.ToString()).Append(" kt");
                        tip.AppendLine().Append("Track: ").Append(Math.Round(livePlane.Track, 0).ToString("000")).Append("°");
                    }
                }
            }
            item.ToolTipText = tip.ToString();
        }

        private static bool IsKstLoginFailureStatus(string status)
        {
            if (String.IsNullOrWhiteSpace(status)) return false;
            string text = status.ToUpperInvariant();
            return text.Contains("KST LOGIN FAILED") ||
                   text.Contains("INVALID KST PASSWORD") ||
                   text.Contains("WRONG PASSWORD") ||
                   text.Contains("BAD PASSWORD") ||
                   text.Contains("INVALID PASSWORD") ||
                   text.Contains("LOGIN INCORRECT") ||
                   text.Contains("ACCESS DENIED") ||
                   text.Contains("AUTHENTICATION FAILED") ||
                   text.Contains("INVALID CALL") ||
                   text.Contains("INVALID USER") ||
                   text.Contains("UNKNOWN USER") ||
                   text.Contains("NO SUCH USER") ||
                   text.Contains("CALLSIGN NOT FOUND") ||
                   text.Contains("NOT REGISTERED") ||
                   text.Contains("BAD LOGIN") ||
                   text.Contains("WRONG LOGIN") ||
                   text.Contains("INVALID LOGIN");
        }

        private void SetConnectingUi()
        {
            _connectButton.Enabled = false;
            _disconnectButton.Enabled = true;
            _sendButton.Enabled = false;
            _cqButton.Enabled = false;
            if (_composeBox != null) _composeBox.Enabled = false;
            if (_macroButtons != null)
            {
                foreach (Button b in _macroButtons) if (b != null) b.Enabled = false;
            }
            _setupButton.Enabled = false;
            if (_hostBox != null) _hostBox.Enabled = false;
            if (_portBox != null) _portBox.Enabled = false;
            if (_userBox != null) _userBox.Enabled = false;
            if (_passBox != null) _passBox.Enabled = false;
            if (_roomCombo != null) _roomCombo.Enabled = false;
        }

        private void DisposeKstClient()
        {
            TelnetKstClient client = _kst;
            _kst = null;
            if (client == null) return;
            try { client.LineReceived -= OnKstLineReceived; } catch { }
            try { client.StatusChanged -= OnKstStatusChanged; } catch { }
            try { client.LoggedIn -= OnKstLoggedIn; } catch { }
            try { client.Dispose(); } catch { }
        }

        private void HandleKstConnectionFailure(string status, string popupText)
        {
            if (_kstFailureHandling) return;
            _kstFailureHandling = true;
            try
            {
                _kstDisconnectRequested = true;
                _kstLoginComplete = false;
                if (_kstLoginTimeoutTimer != null) _kstLoginTimeoutTimer.Stop();
                _userRefreshTimer.Stop();
                AbortUserRefresh(null);
                DisposeKstClient();
                SetConnectionUi(false);
                UpdateStatus(status);
                if (_mainForm != null) _mainForm.SetMainStatusText(status);

                if (!_kstFailurePopupShown && !IsDisposed)
                {
                    _kstFailurePopupShown = true;
                    MessageBox.Show(
                        this,
                        popupText,
                        "KST Chat connection failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            finally
            {
                _kstFailureHandling = false;
            }
        }

        private void HandleKstConnectionLost(string status)
        {
            _kstDisconnectRequested = true;
            _kstLoginComplete = false;
            if (_kstLoginTimeoutTimer != null) _kstLoginTimeoutTimer.Stop();
            _userRefreshTimer.Stop();
            AbortUserRefresh(null);
            DisposeKstClient();
            SetConnectionUi(false);
            UpdateStatus(status);
            if (_mainForm != null) _mainForm.SetMainStatusText(status);
        }

        private void SetConnectionUi(bool connected)
        {
            _connectButton.Enabled = !connected;
            _disconnectButton.Enabled = connected;
            _sendButton.Enabled = connected;
            _cqButton.Enabled = connected;
            if (_composeBox != null) _composeBox.Enabled = connected;
            if (_macroButtons != null)
            {
                foreach (Button b in _macroButtons) if (b != null) b.Enabled = connected;
            }
            // Setup contains host/port/user/password. Keep it locked while connected.
            _setupButton.Enabled = !connected;
            if (_hostBox != null) _hostBox.Enabled = !connected;
            if (_portBox != null) _portBox.Enabled = !connected;
            if (_userBox != null) _userBox.Enabled = !connected;
            if (_passBox != null) _passBox.Enabled = !connected;
            // Room switching is allowed while connected; it reconnects automatically.
            if (_roomCombo != null) _roomCombo.Enabled = true;
        }

        private void RecordMapRender(long milliseconds)
        {
            _perfLastMapRenderMs = Math.Max(0, milliseconds);
            if (_perfLastMapRenderMs > _perfMaxMapRenderMs) _perfMaxMapRenderMs = _perfLastMapRenderMs;
        }

        private void ShowPerformanceDiagnostics()
        {
            string text =
                "KST users: " + _userMap.Count.ToString() + Environment.NewLine +
                "Visible rows: " + (_users == null ? "0" : _users.Items.Count.ToString()) + Environment.NewLine +
                "AirScout results: " + _airScoutResults.Count.ToString() + Environment.NewLine +
                "AirScout reply: " + _perfLastAirScoutReplyMs.ToString() + " ms" + Environment.NewLine +
                "Aircraft feed: " + _perfLastPlaneFetchMs.ToString() + " ms" + Environment.NewLine +
                "KST user refresh: " + _perfLastKstRefreshMs.ToString() + " ms" + Environment.NewLine +
                "Map render: " + _perfLastMapRenderMs.ToString() + " ms (max " + _perfMaxMapRenderMs.ToString() + " ms)" + Environment.NewLine +
                "UI delay: " + _perfLastUiDelayMs.ToString() + " ms (max " + _perfMaxUiDelayMs.ToString() + " ms)" + Environment.NewLine +
                "Aircraft status: " + (_airScoutPlaneFeedStatus ?? "unknown") + Environment.NewLine +
                "Watched stations: " + _watchedCalls.Count.ToString();
            MessageBox.Show(this, text, "KST Bridge performance", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void UpdateStatus(string text)
        {
            if (_statusLabel != null) _statusLabel.Text = DateTime.UtcNow.ToString("HH:mm:ss") + " UTC  " + text;
        }

        private void SafeUi(Action action)
        {
            if (IsDisposed) return;
            try
            {
                if (InvokeRequired) BeginInvoke(action); else action();
            }
            catch { }
        }


        private void BeginRefreshAirScoutLivePlanes(bool force)
        {
            if (_settings == null || !_settings.AirScoutEnabled) return;
            int httpPort = _settings.AirScoutHttpPort > 0 ? _settings.AirScoutHttpPort : 9880;
            lock (_airScoutPlaneLock)
            {
                if (_airScoutPlaneFetchRunning) return;
                if (!force && _lastAirScoutPlaneFetchUtc != DateTime.MinValue &&
                    DateTime.UtcNow - _lastAirScoutPlaneFetchUtc < TimeSpan.FromSeconds(5)) return;
                _airScoutPlaneFetchRunning = true;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                Stopwatch planeFetchWatch = Stopwatch.StartNew();
                try
                {
                    string url = "http://127.0.0.1:" + httpPort.ToString() + "/planes.json";
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    request.Method = "GET";
                    request.Proxy = null;
                    request.Timeout = 3000;
                    request.ReadWriteTimeout = 3000;
                    request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                    string json;
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    using (Stream stream = response.GetResponseStream())
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                        json = reader.ReadToEnd();

                    Dictionary<string, AirScoutLivePlane> parsed = ParseAirScoutPlanesJson(json);
                    lock (_airScoutPlaneLock)
                    {
                        int parsedUnique = new HashSet<AirScoutLivePlane>(parsed.Values).Count;
                        if (parsedUnique > 0)
                        {
                            _airScoutPlaneById.Clear();
                            foreach (KeyValuePair<string, AirScoutLivePlane> kv in parsed)
                                _airScoutPlaneById[kv.Key] = kv.Value;
                            _airScoutEmptyPlaneFetches = 0;
                            _lastGoodAirScoutPlaneFetchUtc = DateTime.UtcNow;
                            _airScoutPlaneFeedStatus = parsedUnique.ToString() + " live aircraft";
                        }
                        else
                        {
                            // AirScout can briefly expose an empty planes.json while its
                            // own plane feed is being replaced.  Keep the previous good
                            // snapshot so all aircraft do not flash off the map.
                            _airScoutEmptyPlaneFetches++;
                            int retained = new HashSet<AirScoutLivePlane>(_airScoutPlaneById.Values).Count;
                            _airScoutPlaneFeedStatus = retained > 0
                                ? retained.ToString() + " live aircraft (last good)"
                                : "Aircraft feed returned no positions";
                        }
                        _lastAirScoutPlaneFetchUtc = DateTime.UtcNow;
                    }
                    SafeUi(delegate
                    {
                        if (_settings != null && _settings.AircraftFilterMode >= (int)KstAircraftFilterMode.PassengerOrCargo)
                        {
                            RefreshAllAirScoutCells();
                            RebuildVisibleUserList();
                            if (_settings.AirScoutAutoSort) SortUsersByAirScoutOpportunity();
                        }
                        RefreshMapAircraftOnly();
                    });
                }
                catch (Exception ex)
                {
                    lock (_airScoutPlaneLock)
                    {
                        _lastAirScoutPlaneFetchUtc = DateTime.UtcNow;
                        int retained = new HashSet<AirScoutLivePlane>(_airScoutPlaneById.Values).Count;
                        _airScoutPlaneFeedStatus = retained > 0
                            ? retained.ToString() + " live aircraft (feed retry)"
                            : "Aircraft feed: " + ex.Message;
                    }
                    // Do not clear the displayed aircraft on a transient HTTP failure.
                    SafeUi(delegate { RefreshMapAircraftOnly(); });
                }
                finally
                {
                    planeFetchWatch.Stop();
                    _perfLastPlaneFetchMs = planeFetchWatch.ElapsedMilliseconds;
                    lock (_airScoutPlaneLock) _airScoutPlaneFetchRunning = false;
                }
            });
        }

        private static Dictionary<string, AirScoutLivePlane> ParseAirScoutPlanesJson(string json)
        {
            Dictionary<string, AirScoutLivePlane> byId = new Dictionary<string, AirScoutLivePlane>(StringComparer.OrdinalIgnoreCase);
            if (String.IsNullOrWhiteSpace(json)) return byId;

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = Int32.MaxValue;
            object rootObject = serializer.DeserializeObject(json);
            Dictionary<string, object> root = rootObject as Dictionary<string, object>;
            if (root == null) return byId;

            long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (KeyValuePair<string, object> kv in root)
            {
                object[] values = kv.Value as object[];
                if (values == null || values.Length < 11) continue;

                double lat = ToDouble(values, 1, Double.NaN);
                double lon = ToDouble(values, 2, Double.NaN);
                if (Double.IsNaN(lat) || Double.IsNaN(lon) || lat < -90 || lat > 90 || lon < -180 || lon > 180) continue;

                long reportedUnix = ToLong(values, 10, 0);
                if (reportedUnix > 0 && nowUnix - reportedUnix > 600) continue;

                AirScoutLivePlane plane = new AirScoutLivePlane
                {
                    Hex = ToText(values, 0),
                    Lat = lat,
                    Lon = lon,
                    Track = ToDouble(values, 3, 0),
                    AltitudeFt = (int)Math.Round(ToDouble(values, 4, 0), 0),
                    SpeedKt = (int)Math.Round(ToDouble(values, 5, 0), 0),
                    Type = ToText(values, 8),
                    Registration = ToText(values, 9),
                    ReportedUnix = reportedUnix,
                    Flight = ToText(values, 13),
                    Call = ToText(values, 16)
                };

                AddAirScoutPlaneKey(byId, plane.Call, plane);
                AddAirScoutPlaneKey(byId, plane.Flight, plane);
                AddAirScoutPlaneKey(byId, plane.Registration, plane);
                AddAirScoutPlaneKey(byId, plane.Hex, plane);
            }
            return byId;
        }

        private static string ToText(object[] values, int index)
        {
            if (values == null || index < 0 || index >= values.Length || values[index] == null) return "";
            return Convert.ToString(values[index], System.Globalization.CultureInfo.InvariantCulture).Trim();
        }

        private static double ToDouble(object[] values, int index, double fallback)
        {
            if (values == null || index < 0 || index >= values.Length || values[index] == null) return fallback;
            try { return Convert.ToDouble(values[index], System.Globalization.CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        private static long ToLong(object[] values, int index, long fallback)
        {
            if (values == null || index < 0 || index >= values.Length || values[index] == null) return fallback;
            try { return Convert.ToInt64(values[index], System.Globalization.CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        private static string NormalizeAircraftId(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "";
            return Regex.Replace(value.Trim().ToUpperInvariant(), "[^A-Z0-9]+", "");
        }

        private static void AddAirScoutPlaneKey(Dictionary<string, AirScoutLivePlane> byId, string key, AirScoutLivePlane plane)
        {
            key = NormalizeAircraftId(key);
            if (String.IsNullOrWhiteSpace(key) || plane == null) return;
            byId[key] = plane;
        }

        private List<KstMapAircraft> GetSelectedAirScoutAircraftSnapshot()
        {
            List<KstMapAircraft> result = new List<KstMapAircraft>();
            string selectedCall = CleanCall(_lastSelectedCall);
            if (String.IsNullOrWhiteSpace(selectedCall)) return result;

            AirScoutPathResult pathResult;
            if (!_airScoutResults.TryGetValue(selectedCall, out pathResult) || pathResult == null) return result;

            Dictionary<string, AirScoutLivePlane> liveById;
            lock (_airScoutPlaneLock)
                liveById = new Dictionary<string, AirScoutLivePlane>(_airScoutPlaneById, StringComparer.OrdinalIgnoreCase);

            HashSet<AirScoutLivePlane> used = new HashSet<AirScoutLivePlane>();
            foreach (AirScoutPlaneInfo candidate in pathResult.Planes)
            {
                if (candidate == null || candidate.Mins >= 30 || !MatchesAircraftFilter(candidate)) continue;
                string key = NormalizeAircraftId(candidate.Call);
                AirScoutLivePlane live;
                if (String.IsNullOrWhiteSpace(key) || !liveById.TryGetValue(key, out live) || live == null || !used.Add(live)) continue;
                KstAircraftTrafficClass trafficClass = ClassifyAircraftTraffic(live, candidate);
                result.Add(new KstMapAircraft
                {
                    Call = !String.IsNullOrWhiteSpace(candidate.Call) ? candidate.Call.Trim() : live.DisplayName,
                    Flight = live.Flight ?? "",
                    Registration = live.Registration ?? "",
                    Type = live.Type ?? "",
                    Model = GetAircraftModelName(live.Type),
                    Size = GetAirScoutSizeText(candidate),
                    TrafficClass = GetAircraftTrafficClassText(trafficClass),
                    Lat = live.Lat,
                    Lon = live.Lon,
                    Track = live.Track,
                    AltitudeFt = live.AltitudeFt,
                    SpeedKt = live.SpeedKt,
                    Mins = candidate.Mins,
                    Potential = candidate.Potential,
                    IntQRB = candidate.IntQRB,
                    Category = candidate.Category ?? ""
                });
            }
            result.Sort(delegate(KstMapAircraft a, KstMapAircraft b)
            {
                int c = a.Mins.CompareTo(b.Mins);
                if (c != 0) return c;
                return b.Potential.CompareTo(a.Potential);
            });
            return result;
        }

        private bool SelectedAirScoutPathHasCandidates()
        {
            string selectedCall = CleanCall(_lastSelectedCall);
            if (String.IsNullOrWhiteSpace(selectedCall)) return false;
            AirScoutPathResult pathResult;
            if (!_airScoutResults.TryGetValue(selectedCall, out pathResult) || pathResult == null || pathResult.Planes == null) return false;
            foreach (AirScoutPlaneInfo candidate in pathResult.Planes)
                if (candidate != null && candidate.Mins < 30 && MatchesAircraftFilter(candidate)) return true;
            return false;
        }

        private string GetAirScoutPlaneFeedStatus()
        {
            lock (_airScoutPlaneLock) return _airScoutPlaneFeedStatus;
        }

        private bool TryGetLivePlaneForAirScoutCandidate(string aircraftCall, out AirScoutLivePlane plane)
        {
            plane = null;
            string key = NormalizeAircraftId(aircraftCall);
            if (String.IsNullOrWhiteSpace(key)) return false;
            lock (_airScoutPlaneLock)
            {
                AirScoutLivePlane found;
                if (_airScoutPlaneById.TryGetValue(key, out found) && found != null)
                {
                    plane = found;
                    return true;
                }
            }
            return false;
        }


        private void ShowMapWindow()
        {
            try
            {
                if (_mapForm != null && !_mapForm.IsDisposed)
                {
                    _mapForm.RefreshStations();
                    _mapForm.Show();
                    _mapForm.BringToFront();
                    return;
                }

                _mapForm = new KstUserMapForm(this);
                _mapForm.FormClosed += delegate { _mapForm = null; };
                _mapForm.Show(this);
            }
            catch (Exception ex)
            {
                UpdateStatus("Map open failed: " + ex.Message);
            }
        }

        private void CloseMapWindow()
        {
            try
            {
                KstUserMapForm map = _mapForm;
                _mapForm = null;

                if (map != null && !map.IsDisposed)
                    map.Close();
            }
            catch { }
        }

        private void RefreshMapWindow()
        {
            try
            {
                if (_mapForm != null && !_mapForm.IsDisposed)
                    _mapForm.RefreshStations();
            }
            catch { }
        }

        private void RefreshMapAircraftOnly()
        {
            try
            {
                if (_mapForm != null && !_mapForm.IsDisposed)
                    _mapForm.RefreshAircraftOnly();
            }
            catch { }
        }

        private List<KstMapStation> GetMapStationsSnapshot()
        {
            List<KstMapStation> result = new List<KstMapStation>();
            try
            {
                // The operator is drawn separately using the dedicated home-station
                // marker and colour. Exclude the same callsign from the normal KST
                // station pass so the map never shows two dots or labels at home.
                string ownCall = _settings != null ? CleanCall(_settings.Callsign) : "";

                foreach (KstUserInfo user in _userMap.Values)
                {
                    if (user == null || !IsUserVisibleForCurrentFilter(user) || String.IsNullOrWhiteSpace(user.Call) || !IsValidLocator(user.Locator)) continue;
                    string userCall = CleanCall(user.Call);
                    if (!String.IsNullOrWhiteSpace(ownCall) && String.Equals(userCall, ownCall, StringComparison.OrdinalIgnoreCase)) continue;

                    string qtf;
                    string qrb;
                    CalculateQtfQrb(user.Locator, out qtf, out qrb);
                    GeoPoint pos = LocatorToPoint(user.Locator);
                    result.Add(new KstMapStation
                    {
                        Call = userCall,
                        Name = user.Name ?? "",
                        Locator = NormalizeLocator(user.Locator),
                        Lat = pos.Lat,
                        Lon = pos.Lon,
                        Qtf = qtf,
                        Qrb = qrb,
                        Worked = user.WorkedCurrentBand,
                        IsActive = user.LastActivityUtc != DateTime.MinValue,
                        IsSelected = String.Equals(CleanCall(user.Call), CleanCall(_lastSelectedCall), StringComparison.OrdinalIgnoreCase),
                        IsWatched = IsWatchedCall(userCall)
                    });
                }
            }
            catch { }
            result.Sort(delegate(KstMapStation a, KstMapStation b) { return String.Compare(a.Call, b.Call, StringComparison.OrdinalIgnoreCase); });
            return result;
        }

        private bool TryGetOwnMapPoint(out KstMapStation own)
        {
            own = null;
            try
            {
                string loc = NormalizeLocator(GetOwnLocator());
                if (!IsValidLocator(loc)) return false;
                GeoPoint pos = LocatorToPoint(loc);
                own = new KstMapStation
                {
                    Call = _settings != null ? CleanCall(_settings.Callsign) : "ME",
                    Name = _settings != null ? (_settings.Name ?? "") : "",
                    Locator = loc,
                    Lat = pos.Lat,
                    Lon = pos.Lon,
                    Qtf = "0°",
                    Qrb = "0 km",
                    Worked = false,
                    IsActive = true,
                    IsSelected = false
                };
                return true;
            }
            catch { return false; }
        }

        private void SelectStationFromMap(KstMapStation station, bool turnRotator)
        {
            if (station == null || String.IsNullOrWhiteSpace(station.Call)) return;
            _lastSelectedCall = station.Call;

            try
            {
                foreach (ListViewItem item in _users.Items)
                {
                    bool match = String.Equals(CleanCall(item.Text), CleanCall(station.Call), StringComparison.OrdinalIgnoreCase);
                    item.Selected = match;
                    if (match) item.EnsureVisible();
                }
                RestyleUsers();
                RefreshConversationView();
            }
            catch { }

            if (turnRotator)
            {
                string queuedCall = station.Call;
                string queuedLocator = station.Locator;
                PutCallIntoDxLog(queuedCall, queuedLocator, delegate
                {
                    TurnRotatorToStation(queuedCall, queuedLocator);
                });
            }
            else
            {
                PutCallIntoDxLog(station.Call, station.Locator);
            }
        }

        private bool TryGetAzimuthToLocator(string locator, out int azimuth)
        {
            azimuth = 0;
            try
            {
                string myLocator = NormalizeLocator(GetOwnLocator());
                locator = NormalizeLocator(locator);
                if (!IsValidLocator(myLocator) || !IsValidLocator(locator)) return false;
                GeoPoint here = LocatorToPoint(myLocator);
                GeoPoint there = LocatorToPoint(locator);
                azimuth = (int)Math.Round(AzimuthDegrees(here, there), 0);
                if (azimuth >= 360) azimuth = 0;
                return true;
            }
            catch { return false; }
        }

        private void TurnRotatorToStation(string call, string locator)
        {
            try
            {
                FrmMain resolvedMain = ResolveDxLogMainForm();
                if (resolvedMain == null)
                {
                    UpdateStatus("DXLog main form not found - cannot turn rotator");
                    return;
                }
                _mainForm = resolvedMain;

                int azimuth;
                if (!TryGetAzimuthToLocator(locator, out azimuth))
                {
                    UpdateStatus("No valid bearing for " + call);
                    return;
                }

                // DXLog's normal rotator command is Ctrl+F12, which runs
                // turnAntennaToLoggedCallShortPathToolStripMenuItem_Click().
                // Use that same DXLog command rather than trying to drive the
                // rotator object directly. The short delay lets DXLog finish the
                // callsign/QRA lookup after PutCallIntoDxLog() has populated the
                // active entry line.
                string cleanCall = CleanCall(call);
                System.Windows.Forms.Timer rotatorTimer = new System.Windows.Forms.Timer();
                rotatorTimer.Interval = 250;
                rotatorTimer.Tick += delegate
                {
                    rotatorTimer.Stop();
                    rotatorTimer.Dispose();

                    try
                    {
                        MethodInfo dxLogCtrlF12 = null;
                        foreach (MethodInfo candidate in typeof(FrmMain).GetMethods(
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (String.Equals(candidate.Name,
                                    "turnAntennaToLoggedCallShortPathToolStripMenuItem_Click",
                                    StringComparison.OrdinalIgnoreCase) ||
                                candidate.Name.IndexOf("turnAntennaToLoggedCallShortPath",
                                    StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                dxLogCtrlF12 = candidate;
                                break;
                            }
                        }

                        if (dxLogCtrlF12 != null)
                        {
                            try
                            {
                                ParameterInfo[] parameters = dxLogCtrlF12.GetParameters();
                                if (parameters.Length == 0)
                                    dxLogCtrlF12.Invoke(_mainForm, null);
                                else if (parameters.Length == 2)
                                    dxLogCtrlF12.Invoke(_mainForm, new object[] { _mainForm, EventArgs.Empty });
                                else
                                    dxLogCtrlF12 = null;

                                if (dxLogCtrlF12 != null)
                                {
                                    UpdateStatus("Selected " + cleanCall + " and triggered DXLog Ctrl+F12 rotator command (" + azimuth.ToString() + "°)");
                                    return;
                                }
                            }
                            catch
                            {
                                // Continue to the keyboard and direct-azimuth
                                // fallbacks if this DXLog build has a changed handler.
                            }
                        }

                        // Fallback for older/newer DXLog builds where the menu
                        // handler name changes: focus DXLog and send Ctrl+F12.
                        try
                        {
                            _mainForm.Activate();
                            _mainForm.Focus();
                            SendKeys.SendWait("^{F12}");
                            UpdateStatus("Selected " + cleanCall + " and sent Ctrl+F12 to DXLog (" + azimuth.ToString() + "°)");
                            return;
                        }
                        catch { }

                        // Final fallback: previous direct azimuth call. This is
                        // kept only as a backup; the preferred route is DXLog's
                        // own Ctrl+F12 command above.
                        string band = "";
                        int radioNumber = 1;
                        try
                        {
                            if (_contestData != null)
                            {
                                band = Convert.ToString(_contestData.FocusedRadioBand) ?? "";
                                radioNumber = Convert.ToInt32(_contestData.FocusedRadio);
                                if (radioNumber < 1) radioNumber = 1;
                            }
                        }
                        catch { radioNumber = 1; }

                        MethodInfo direct = typeof(FrmMain).GetMethod("RotatorSetAzimuth", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (direct == null)
                        {
                            UpdateStatus("DXLog rotator command not found");
                            return;
                        }

                        direct.Invoke(_mainForm, new object[] { band, radioNumber, null, cleanCall, azimuth, false });
                        UpdateStatus("Selected " + cleanCall + " and sent direct rotator azimuth " + azimuth.ToString() + "°");
                    }
                    catch (Exception ex)
                    {
                        UpdateStatus("Rotator command failed: " + ex.Message);
                    }
                };
                rotatorTimer.Start();
            }
            catch (Exception ex)
            {
                UpdateStatus("Rotator command failed: " + ex.Message);
            }
        }

        private sealed class KstMapStation
        {
            public string Call;
            public string Name;
            public string Locator;
            public double Lat;
            public double Lon;
            public string Qtf;
            public string Qrb;
            public bool Worked;
            public bool IsActive;
            public bool IsSelected;
            public bool IsWatched;
        }

        private sealed class KstMapAircraft
        {
            public string Call;
            public string Flight;
            public string Registration;
            public string Type;
            public string Model;
            public string Size;
            public string TrafficClass;
            public string Category;
            public double Lat;
            public double Lon;
            public double Track;
            public int AltitudeFt;
            public int SpeedKt;
            public int Mins;
            public int Potential;
            public int IntQRB;
        }

        private sealed class KstUserMapForm : Form
        {
            private readonly KstChatBridge _owner;
            private readonly KstMapCanvas _canvas;
            private readonly CheckBox _turnRotator;
            private readonly CheckBox _showAirScout;
            private readonly CheckBox _showTrails;
            private readonly Button _refreshButton;
            private readonly Button _zoomInButton;
            private readonly Button _zoomOutButton;
            private readonly Button _zoomResetButton;
            private readonly Label _status;
            private readonly ToolTip _toolTip;
            private readonly System.Windows.Forms.Timer _refreshTimer;
            private readonly Icon _mapIcon;
            private string _aircraftForCall = "";
            private DateTime _lastGoodMatchedAircraftUtc = DateTime.MinValue;

            public KstUserMapForm(KstChatBridge owner)
            {
                _owner = owner;
                Text = "KST Users Map";
                StartPosition = FormStartPosition.CenterParent;
                Size = new Size(1180, 620);
                MinimumSize = new Size(1120, 360);
                Font = owner._windowFont;
                _mapIcon = CreateGlobeIcon();
                if (_mapIcon != null) Icon = _mapIcon;

                TableLayoutPanel layout = new TableLayoutPanel();
                layout.Dock = DockStyle.Fill;
                layout.RowCount = 3;
                layout.ColumnCount = 9;
                layout.Padding = new Padding(6);
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

                // Keep every map command in its own non-shrinking column.  The
                // flexible spacer is deliberately placed before Close so a
                // narrow window can never collapse the AirScout controls
                // underneath the Close button.
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

                _refreshButton = new Button { Text = "Refresh", Dock = DockStyle.Fill, Margin = new Padding(3) };
                _zoomInButton = new Button { Text = "Zoom +", Dock = DockStyle.Fill, Margin = new Padding(3) };
                _zoomOutButton = new Button { Text = "Zoom -", Dock = DockStyle.Fill, Margin = new Padding(3) };
                _zoomResetButton = new Button { Text = "Fit", Dock = DockStyle.Fill, Margin = new Padding(3) };
                _turnRotator = new CheckBox { Text = "Rotator", Checked = owner._settings == null || owner._settings.TurnRotatorOnStationClick, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(6, 0, 3, 0) };
                _showAirScout = new CheckBox { Text = "Show AS", Checked = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(6, 0, 3, 0) };
                _showTrails = new CheckBox { Text = "AS Trails", Checked = owner._settings == null || owner._settings.ShowAircraftTrails, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(6, 0, 3, 0) };
                _status = new Label { Text = "Click a station to select it in DXLog", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

                _canvas = new KstMapCanvas(owner);
                _canvas.Dock = DockStyle.Fill;
                _canvas.StationClicked += delegate(KstMapStation station) { StationClicked(station); };
                _canvas.HoverTextChanged += delegate(string text)
                {
                    _toolTip.SetToolTip(_canvas, String.IsNullOrWhiteSpace(text)
                        ? "Click a station marker to select it; right-click a station marker for KST Bridge commands; drag to pan"
                        : text);
                };

                layout.Controls.Add(_refreshButton, 0, 0);
                layout.Controls.Add(_zoomInButton, 1, 0);
                layout.Controls.Add(_zoomOutButton, 2, 0);
                layout.Controls.Add(_zoomResetButton, 3, 0);
                layout.Controls.Add(_turnRotator, 4, 0);
                layout.Controls.Add(_showAirScout, 5, 0);
                layout.Controls.Add(_showTrails, 6, 0);
                Button closeButton = new Button { Text = "Close", Dock = DockStyle.Fill };
                closeButton.Click += delegate { Close(); };
                layout.Controls.Add(closeButton, 8, 0);
                layout.Controls.Add(_canvas, 0, 1); layout.SetColumnSpan(_canvas, 9);
                layout.Controls.Add(_status, 0, 2); layout.SetColumnSpan(_status, 9);
                Controls.Add(layout);

                _toolTip = new ToolTip { AutoPopDelay = 12000, InitialDelay = 250, ReshowDelay = 100, ShowAlways = true };
                _toolTip.SetToolTip(_refreshButton, "Refresh station markers, the selected path and matched aircraft now");
                _toolTip.SetToolTip(_zoomInButton, "Zoom in around your home station");
                _toolTip.SetToolTip(_zoomOutButton, "Zoom out around your home station");
                _toolTip.SetToolTip(_zoomResetButton, "Fit the visible KST stations and selected AirScout path on the map");
                _toolTip.SetToolTip(_turnRotator, "When enabled, a manual station click or Prepare contact loads the KST QRA into DXLog and turns the rotator");
                _toolTip.SetToolTip(_showAirScout, "Show or hide the selected AirScout path and its matched aircraft. AirScout scanning continues while hidden");
                _toolTip.SetToolTip(_showTrails, "Show or hide the approximately 90-second movement trail behind each matched aircraft; aircraft remain visible when trails are off");
                _toolTip.SetToolTip(closeButton, "Close the KST map window");
                _toolTip.SetToolTip(_canvas, "Click a station marker to select it; right-click a station marker for KST Bridge commands; drag to pan");
                _toolTip.SetToolTip(_status, "Selected path, matched-aircraft count, live-aircraft count and feed status");

                _refreshButton.Click += delegate { RefreshStations(); };
                _zoomInButton.Click += delegate { _canvas.ZoomIn(); UpdateZoomStatus(); };
                _zoomOutButton.Click += delegate { _canvas.ZoomOut(); UpdateZoomStatus(); };
                _zoomResetButton.Click += delegate { _canvas.ResetZoom(); UpdateZoomStatus(); };
                _showAirScout.CheckedChanged += delegate { RefreshStations(); };
                _turnRotator.CheckedChanged += delegate
                {
                    if (_owner._settings != null)
                    {
                        _owner._settings.TurnRotatorOnStationClick = _turnRotator.Checked;
                        _owner._settings.Save();
                    }
                };
                _showTrails.CheckedChanged += delegate
                {
                    _canvas.ShowAircraftTrails = _showTrails.Checked;
                    if (_owner._settings != null)
                    {
                        _owner._settings.ShowAircraftTrails = _showTrails.Checked;
                        _owner._settings.Save();
                    }
                    _canvas.AircraftChanged();
                };
                _canvas.ShowAircraftTrails = _showTrails.Checked;
                Shown += delegate { RefreshStations(); };

                _refreshTimer = new System.Windows.Forms.Timer();
                _refreshTimer.Interval = 5000;
                _refreshTimer.Tick += delegate { RefreshAircraftOnly(); };
                _refreshTimer.Start();
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                if (_refreshTimer != null) _refreshTimer.Stop();
                if (_toolTip != null) _toolTip.Dispose();
                if (_mapIcon != null) _mapIcon.Dispose();
                base.OnFormClosed(e);
            }

            public void HandleBridgeRightClick(Point screenPoint)
            {
                try
                {
                    Point canvasPoint = _canvas.PointToClient(screenPoint);
                    if (_canvas.ClientRectangle.Contains(canvasPoint))
                        _canvas.ShowStationContextMenu(canvasPoint);
                }
                catch { }
            }

            private static Icon CreateGlobeIcon()
            {
                try
                {
                    Bitmap bmp = new Bitmap(32, 32);
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);

                        Rectangle globe = new Rectangle(3, 3, 26, 26);
                        using (SolidBrush sea = new SolidBrush(Color.FromArgb(44, 126, 196)))
                        using (Pen outline = new Pen(Color.FromArgb(18, 52, 92), 2))
                        using (Pen grid = new Pen(Color.FromArgb(170, 230, 245, 255), 1))
                        using (SolidBrush land = new SolidBrush(Color.FromArgb(78, 168, 92)))
                        using (SolidBrush land2 = new SolidBrush(Color.FromArgb(62, 145, 78)))
                        {
                            g.FillEllipse(sea, globe);
                            g.DrawEllipse(outline, globe);

                            g.DrawArc(grid, 8, 4, 16, 24, 90, 180);
                            g.DrawArc(grid, 8, 4, 16, 24, 270, 180);
                            g.DrawLine(grid, 16, 4, 16, 29);
                            g.DrawLine(grid, 5, 16, 28, 16);
                            g.DrawArc(grid, 5, 10, 22, 12, 0, 360);

                            Point[] europe = new Point[]
                            {
                                new Point(14, 8), new Point(20, 7), new Point(24, 11),
                                new Point(22, 15), new Point(18, 16), new Point(16, 14),
                                new Point(12, 13), new Point(11, 10)
                            };
                            Point[] africa = new Point[]
                            {
                                new Point(17, 15), new Point(23, 17), new Point(24, 22),
                                new Point(20, 27), new Point(16, 24), new Point(14, 18)
                            };
                            Point[] america = new Point[]
                            {
                                new Point(7, 9), new Point(12, 7), new Point(13, 12),
                                new Point(10, 17), new Point(12, 24), new Point(9, 27),
                                new Point(6, 20), new Point(5, 14)
                            };

                            g.FillPolygon(land, europe);
                            g.FillPolygon(land2, africa);
                            g.FillPolygon(land, america);
                        }

                        using (Pen shine = new Pen(Color.FromArgb(180, Color.White), 2))
                        {
                            g.DrawArc(shine, 7, 6, 14, 10, 195, 70);
                        }
                    }

                    IntPtr hIcon = bmp.GetHicon();
                    Icon icon = (Icon)Icon.FromHandle(hIcon).Clone();
                    DestroyIcon(hIcon);
                    bmp.Dispose();
                    return icon;
                }
                catch
                {
                    return null;
                }
            }

            [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
            private static extern bool DestroyIcon(IntPtr handle);

            public bool TurnRotatorOnClickEnabled
            {
                get { return _turnRotator != null && _turnRotator.Checked; }
            }

            public void RefreshStations()
            {
                try
                {
                    KstMapStation own;
                    _canvas.OwnStation = _owner.TryGetOwnMapPoint(out own) ? own : null;
                    List<KstMapStation> latestStations = _owner.GetMapStationsSnapshot();
                    _canvas.Stations = latestStations;
                    UpdateSelectedPathStation(latestStations);

                    if (_showAirScout.Checked)
                    {
                        _owner.BeginRefreshAirScoutLivePlanes(false);
                        UpdateDisplayedAircraft();
                    }
                    else
                    {
                        _canvas.Aircraft = new List<KstMapAircraft>();
                        _aircraftForCall = "";
                    }

                    _canvas.SceneChanged();
                    UpdateMapStatus();
                }
                catch { }
            }

            public void RefreshAircraftOnly()
            {
                try
                {
                    // Refresh station activity colours, but never discard the stored
                    // selected path merely because a transient KST snapshot omitted it.
                    List<KstMapStation> latestStations = _owner.GetMapStationsSnapshot();
                    bool stationColoursChanged = ActivityStateChanged(_canvas.Stations, latestStations);
                    if (stationColoursChanged)
                    {
                        _canvas.Stations = latestStations;
                        UpdateSelectedPathStation(latestStations);
                    }

                    if (_showAirScout.Checked)
                    {
                        _owner.BeginRefreshAirScoutLivePlanes(false);
                        UpdateDisplayedAircraft();
                    }
                    else
                    {
                        _canvas.Aircraft = new List<KstMapAircraft>();
                        _aircraftForCall = "";
                    }

                    if (stationColoursChanged) _canvas.SceneChanged();
                    else _canvas.AircraftChanged();

                    UpdateMapStatus();
                }
                catch { }
            }

            private void UpdateSelectedPathStation(List<KstMapStation> latestStations)
            {
                string selectedCall = CleanCall(_owner._lastSelectedCall);
                if (String.IsNullOrWhiteSpace(selectedCall))
                {
                    _canvas.SelectedStation = null;
                    _canvas.Aircraft = new List<KstMapAircraft>();
                    _aircraftForCall = "";
                    return;
                }

                KstMapStation match = null;
                foreach (KstMapStation station in latestStations ?? new List<KstMapStation>())
                {
                    if (station != null && String.Equals(CleanCall(station.Call), selectedCall, StringComparison.OrdinalIgnoreCase))
                    {
                        match = station;
                        break;
                    }
                }

                if (match != null)
                {
                    _canvas.SelectedStation = match;
                    return;
                }

                // Keep the previously selected station/path if the current refresh is
                // incomplete.  Only a real selection change or explicit clear removes it.
                if (_canvas.SelectedStation == null ||
                    !String.Equals(CleanCall(_canvas.SelectedStation.Call), selectedCall, StringComparison.OrdinalIgnoreCase))
                {
                    _canvas.SelectedStation = null;
                }
            }

            private void UpdateDisplayedAircraft()
            {
                string selectedCall = CleanCall(_owner._lastSelectedCall);
                if (String.IsNullOrWhiteSpace(selectedCall))
                {
                    _canvas.Aircraft = new List<KstMapAircraft>();
                    _aircraftForCall = "";
                    return;
                }

                List<KstMapAircraft> snapshot = _owner.GetSelectedAirScoutAircraftSnapshot();
                bool selectionChanged = !String.Equals(_aircraftForCall, selectedCall, StringComparison.OrdinalIgnoreCase);
                if (snapshot.Count > 0)
                {
                    _canvas.Aircraft = snapshot;
                    _aircraftForCall = selectedCall;
                    _lastGoodMatchedAircraftUtc = DateTime.UtcNow;
                    return;
                }

                if (selectionChanged)
                {
                    // Never show aircraft belonging to the previously selected path.
                    _canvas.Aircraft = new List<KstMapAircraft>();
                    _aircraftForCall = selectedCall;
                    _lastGoodMatchedAircraftUtc = DateTime.MinValue;
                    return;
                }

                if (!_owner.SelectedAirScoutPathHasCandidates())
                {
                    // AirScout has positively reported no suitable aircraft for this path.
                    _canvas.Aircraft = new List<KstMapAircraft>();
                    _lastGoodMatchedAircraftUtc = DateTime.MinValue;
                    return;
                }

                // A candidate still exists but the live plane snapshot was momentarily
                // unavailable.  Keep the last displayed positions instead of flashing
                // every aircraft off the map.
                if (_lastGoodMatchedAircraftUtc != DateTime.MinValue &&
                    DateTime.UtcNow - _lastGoodMatchedAircraftUtc > TimeSpan.FromSeconds(45))
                {
                    _canvas.Aircraft = new List<KstMapAircraft>();
                    _lastGoodMatchedAircraftUtc = DateTime.MinValue;
                }
            }

            private void UpdateMapStatus()
            {
                if (_canvas.SelectedStation != null && _showAirScout.Checked)
                    _status.Text = _canvas.SelectedStation.Call + " path - " + _canvas.Aircraft.Count.ToString() + " matched aircraft - " + _owner.GetAirScoutPlaneFeedStatus();
                else
                    _status.Text = _canvas.Stations.Count.ToString() + " stations with valid locators";
            }

            private static bool ActivityStateChanged(List<KstMapStation> current, List<KstMapStation> latest)
            {
                if (current == null || latest == null || current.Count != latest.Count) return true;
                Dictionary<string, KstMapStation> byCall = new Dictionary<string, KstMapStation>(StringComparer.OrdinalIgnoreCase);
                foreach (KstMapStation station in current)
                {
                    if (station != null && !String.IsNullOrWhiteSpace(station.Call)) byCall[station.Call] = station;
                }
                foreach (KstMapStation station in latest)
                {
                    if (station == null || String.IsNullOrWhiteSpace(station.Call)) return true;
                    KstMapStation old;
                    if (!byCall.TryGetValue(station.Call, out old)) return true;
                    if (old.IsActive != station.IsActive || old.Worked != station.Worked || old.IsSelected != station.IsSelected || old.IsWatched != station.IsWatched) return true;
                }
                return false;
            }

            private void UpdateZoomStatus()
            {
                try
                {
                    _status.Text = _canvas.Stations.Count.ToString() + " stations with valid locators - zoom " + _canvas.ZoomText;
                }
                catch { }
            }

            private void StationClicked(KstMapStation station)
            {
                if (station == null) return;
                _owner.SelectStationFromMap(station, _turnRotator.Checked);
                RefreshStations();
                _status.Text = "Selected " + station.Call + " " + station.Locator + " " + station.Qtf + " " + station.Qrb;
            }
        }

        private sealed class KstMapCanvas : Panel
        {
            private const int TileSize = 256;
            private const int MinTileZoom = 2;
            private const int MaxTileZoom = 9;

            private readonly KstChatBridge _owner;
            private List<KstMapHit> _hits = new List<KstMapHit>();
            private int _tileZoom = 4;
            private double _centerLat = 54.0;
            private double _centerLon = 0.0;
            private bool _fitPending = true;
            private bool _dragging;
            private bool _dragMoved;
            private Point _dragStart;
            private double _dragStartCenterX;
            private double _dragStartCenterY;
            private Point _dragOffset = Point.Empty;
            private bool _panCommitPending;
            private Bitmap _baseFrame;
            private bool _baseFrameDirty = true;
            private List<RectangleF> _staticLabelBounds = new List<RectangleF>();
            private readonly System.Windows.Forms.Timer _frameRebuildTimer;
            private readonly Dictionary<string, List<KstAircraftTrailPoint>> _aircraftTrails = new Dictionary<string, List<KstAircraftTrailPoint>>(StringComparer.OrdinalIgnoreCase);
            public bool ShowAircraftTrails = true;

            private readonly Dictionary<string, Image> _tileImages = new Dictionary<string, Image>();
            private readonly HashSet<string> _tileDownloads = new HashSet<string>();
            private readonly object _tileLock = new object();

            public List<KstMapStation> Stations = new List<KstMapStation>();
            public KstMapStation OwnStation;
            public KstMapStation SelectedStation;
            public List<KstMapAircraft> Aircraft = new List<KstMapAircraft>();
            public event Action<KstMapStation> StationClicked;
            public event Action<string> HoverTextChanged;
            private string _lastHoverText = "";

            public KstMapCanvas(KstChatBridge owner)
            {
                _owner = owner;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                DoubleBuffered = true;
                ResizeRedraw = true;
                BackColor = Color.FromArgb(218, 235, 243);
                TabStop = true;
                Cursor = Cursors.Hand;
                MouseDown += CanvasMouseDown;
                MouseMove += CanvasMouseMove;
                MouseUp += CanvasMouseUp;
                MouseEnter += delegate { try { Focus(); } catch { } };
                MouseWheel += CanvasMouseWheel;
                _frameRebuildTimer = new System.Windows.Forms.Timer();
                _frameRebuildTimer.Interval = 160;
                _frameRebuildTimer.Tick += delegate
                {
                    _frameRebuildTimer.Stop();
                    RebuildBaseFrame();
                };
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    try { _frameRebuildTimer.Stop(); _frameRebuildTimer.Dispose(); } catch { }
                    try { if (_baseFrame != null) _baseFrame.Dispose(); } catch { }
                    lock (_tileLock)
                    {
                        foreach (Image image in _tileImages.Values) try { image.Dispose(); } catch { }
                        _tileImages.Clear();
                    }
                }
                base.Dispose(disposing);
            }

            protected override void OnResize(EventArgs eventargs)
            {
                base.OnResize(eventargs);
                SceneChanged();
            }

            public void SceneChanged()
            {
                UpdateAircraftTrails();
                _baseFrameDirty = true;
                if (_frameRebuildTimer != null)
                {
                    _frameRebuildTimer.Stop();
                    _frameRebuildTimer.Start();
                }
                Invalidate();
            }

            public void AircraftChanged()
            {
                UpdateAircraftTrails();
                Invalidate();
            }

            private void UpdateAircraftTrails()
            {
                DateTime now = DateTime.UtcNow;
                HashSet<string> present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (KstMapAircraft plane in Aircraft ?? new List<KstMapAircraft>())
                {
                    if (plane == null) continue;
                    string key = CleanCall(plane.Call);
                    if (String.IsNullOrWhiteSpace(key)) continue;
                    present.Add(key);
                    List<KstAircraftTrailPoint> points;
                    if (!_aircraftTrails.TryGetValue(key, out points))
                    {
                        points = new List<KstAircraftTrailPoint>();
                        _aircraftTrails[key] = points;
                    }
                    KstAircraftTrailPoint last = points.Count == 0 ? null : points[points.Count - 1];
                    if (last == null || Math.Abs(last.Lat - plane.Lat) > 0.0005 || Math.Abs(last.Lon - plane.Lon) > 0.0005)
                        points.Add(new KstAircraftTrailPoint { Lat = plane.Lat, Lon = plane.Lon, Utc = now });
                    points.RemoveAll(delegate(KstAircraftTrailPoint p) { return p == null || now - p.Utc > TimeSpan.FromSeconds(90); });
                    while (points.Count > 18) points.RemoveAt(0);
                }
                List<string> remove = new List<string>();
                foreach (KeyValuePair<string, List<KstAircraftTrailPoint>> kv in _aircraftTrails)
                {
                    kv.Value.RemoveAll(delegate(KstAircraftTrailPoint p) { return p == null || now - p.Utc > TimeSpan.FromSeconds(90); });
                    if (kv.Value.Count == 0 && !present.Contains(kv.Key)) remove.Add(kv.Key);
                }
                foreach (string key in remove) _aircraftTrails.Remove(key);
            }

            public string ZoomText
            {
                get { return "z" + _tileZoom.ToString(); }
            }

            private void CentreZoomOnHome()
            {
                // Zooming is always anchored on the operator's home station.
                // A deliberate drag may pan the map temporarily, but the next
                // zoom-in or zoom-out returns the viewport centre to home.
                if (OwnStation == null) return;
                _centerLat = Clamp(OwnStation.Lat, -82, 82);
                _centerLon = NormalizeLon(OwnStation.Lon);
            }

            public void ZoomIn()
            {
                _fitPending = false;
                CentreZoomOnHome();
                _tileZoom = Math.Min(MaxTileZoom, _tileZoom + 1);
                SceneChanged();
            }

            public void ZoomOut()
            {
                _fitPending = false;
                CentreZoomOnHome();
                _tileZoom = Math.Max(MinTileZoom, _tileZoom - 1);
                SceneChanged();
            }

            public void ResetZoom()
            {
                _fitPending = true;
                SceneChanged();
            }

            private void CanvasMouseWheel(object sender, MouseEventArgs e)
            {
                if (e.Delta > 0) ZoomIn();
                else if (e.Delta < 0) ZoomOut();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.Clear(BackColor);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                if ((_baseFrame == null || _baseFrameDirty) && !_frameRebuildTimer.Enabled && !_dragging)
                    RebuildBaseFrame();

                if (_baseFrame != null)
                    g.DrawImageUnscaled(_baseFrame, _dragOffset.X, _dragOffset.Y);

                Rectangle area = ClientRectangle;
                area.Inflate(-10, -10);
                if (area.Width <= 10 || area.Height <= 10) return;

                System.Drawing.Drawing2D.GraphicsState state = g.Save();
                try
                {
                    g.SetClip(area);
                    g.TranslateTransform(_dragOffset.X, _dragOffset.Y);
                    // After a drag is released, keep the already-shifted cached frame
                    // visible until the replacement frame is ready. Aircraft are briefly
                    // withheld during that frame swap so they cannot be projected against
                    // the new centre while the old map image is still on screen.
                    if (!_panCommitPending) DrawAircraft(g, area);
                }
                finally
                {
                    g.Restore(state);
                }
            }

            private void RebuildBaseFrame()
            {
                if (IsDisposed || ClientSize.Width < 40 || ClientSize.Height < 40) return;
                Stopwatch renderWatch = Stopwatch.StartNew();
                Bitmap next = null;
                try
                {
                    Rectangle area = ClientRectangle;
                    area.Inflate(-10, -10);
                    if (area.Width <= 10 || area.Height <= 10) return;
                    if (_fitPending)
                    {
                        FitStations(area);
                        _fitPending = false;
                    }

                    next = new Bitmap(ClientSize.Width, ClientSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    using (Graphics g = Graphics.FromImage(next))
                    {
                        g.Clear(BackColor);
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        System.Drawing.Drawing2D.GraphicsState state = g.Save();
                        try
                        {
                            g.SetClip(area);
                            using (SolidBrush b = new SolidBrush(Color.FromArgb(218, 235, 243))) g.FillRectangle(b, area);
                            DrawOpenStreetMapTiles(g, area);
                            DrawGrid(g, area);
                            DrawRangeRings(g, area);
                            _staticLabelBounds = new List<RectangleF>();
                            DrawSelectedPath(g, area);
                            DrawStations(g, area);
                        }
                        finally
                        {
                            g.Restore(state);
                        }
                        using (Pen border = new Pen(Color.SteelBlue)) g.DrawRectangle(border, area);
                    }

                    Bitmap old = _baseFrame;
                    _baseFrame = next;
                    next = null;
                    _baseFrameDirty = false;
                    _dragOffset = Point.Empty;
                    _panCommitPending = false;
                    if (old != null) old.Dispose();
                }
                catch { }
                finally
                {
                    renderWatch.Stop();
                    _owner.RecordMapRender(renderWatch.ElapsedMilliseconds);
                    if (next != null) next.Dispose();
                    Invalidate();
                }
            }

            private void FitStations(Rectangle area)
            {
                try
                {
                    bool any = false;
                    double minLat = 35, maxLat = 65, minLon = -12, maxLon = 35;
                    Action<double, double> add = delegate(double lat, double lon)
                    {
                        if (!any)
                        {
                            minLat = maxLat = lat;
                            minLon = maxLon = lon;
                            any = true;
                        }
                        else
                        {
                            minLat = Math.Min(minLat, lat);
                            maxLat = Math.Max(maxLat, lat);
                            minLon = Math.Min(minLon, lon);
                            maxLon = Math.Max(maxLon, lon);
                        }
                    };

                    if (OwnStation != null) add(OwnStation.Lat, OwnStation.Lon);
                    foreach (KstMapStation s in Stations) add(s.Lat, s.Lon);
                    if (!any) return;

                    double latPad = Math.Max(2.0, (maxLat - minLat) * 0.18);
                    double lonPad = Math.Max(3.0, (maxLon - minLon) * 0.18);
                    minLat = Math.Max(-82, minLat - latPad);
                    maxLat = Math.Min(82, maxLat + latPad);
                    minLon = Math.Max(-179, minLon - lonPad);
                    maxLon = Math.Min(179, maxLon + lonPad);
                    if ((maxLat - minLat) < 5) { minLat -= 2.5; maxLat += 2.5; }
                    if ((maxLon - minLon) < 5) { minLon -= 2.5; maxLon += 2.5; }

                    _centerLat = Clamp((minLat + maxLat) / 2.0, -82, 82);
                    _centerLon = NormalizeLon((minLon + maxLon) / 2.0);

                    int bestZoom = MinTileZoom;
                    for (int z = MinTileZoom; z <= MaxTileZoom; z++)
                    {
                        PointF nw = LatLonToPixel(maxLat, minLon, z);
                        PointF se = LatLonToPixel(minLat, maxLon, z);
                        double w = Math.Abs(se.X - nw.X);
                        double h = Math.Abs(se.Y - nw.Y);
                        if (w <= area.Width * 0.92 && h <= area.Height * 0.92) bestZoom = z;
                    }
                    _tileZoom = bestZoom;
                }
                catch { }
            }

            private void DrawOpenStreetMapTiles(Graphics g, Rectangle area)
            {
                try
                {
                    PointF center = LatLonToPixel(_centerLat, _centerLon, _tileZoom);
                    double topLeftX = center.X - area.Width / 2.0;
                    double topLeftY = center.Y - area.Height / 2.0;
                    int n = 1 << _tileZoom;

                    int startX = (int)Math.Floor(topLeftX / TileSize);
                    int endX = (int)Math.Floor((topLeftX + area.Width) / TileSize);
                    int startY = (int)Math.Floor(topLeftY / TileSize);
                    int endY = (int)Math.Floor((topLeftY + area.Height) / TileSize);

                    using (SolidBrush sea = new SolidBrush(Color.FromArgb(205, 225, 232))) g.FillRectangle(sea, area);

                    for (int tx = startX; tx <= endX; tx++)
                    {
                        int wrappedX = ((tx % n) + n) % n;
                        for (int ty = startY; ty <= endY; ty++)
                        {
                            if (ty < 0 || ty >= n) continue;
                            int sx = (int)Math.Round(area.Left + tx * TileSize - topLeftX);
                            int sy = (int)Math.Round(area.Top + ty * TileSize - topLeftY);
                            Image img = GetTileImage(_tileZoom, wrappedX, ty);
                            if (img != null)
                                g.DrawImage(img, new Rectangle(sx, sy, TileSize, TileSize));
                            else
                            {
                                using (SolidBrush b = new SolidBrush(Color.FromArgb(226, 236, 238)))
                                    g.FillRectangle(b, sx, sy, TileSize, TileSize);
                                using (Pen p = new Pen(Color.FromArgb(200, 210, 210)))
                                    g.DrawRectangle(p, sx, sy, TileSize, TileSize);
                            }
                        }
                    }
                }
                catch { }
            }

            private Image GetTileImage(int z, int x, int y)
            {
                string key = z.ToString() + "_" + x.ToString() + "_" + y.ToString();
                lock (_tileLock)
                {
                    Image cached;
                    if (_tileImages.TryGetValue(key, out cached)) return cached;
                }

                string path = GetTilePath(z, x, y);
                if (File.Exists(path))
                {
                    try
                    {
                        using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (Image src = Image.FromStream(fs))
                        {
                            Image clone = new Bitmap(src);
                            lock (_tileLock) _tileImages[key] = clone;
                            return clone;
                        }
                    }
                    catch { try { File.Delete(path); } catch { } }
                }

                StartTileDownload(z, x, y, key, path);
                return null;
            }

            private static string TileCacheDirectory
            {
                get
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DXLog.net", "KstMapTiles");
                    try { Directory.CreateDirectory(dir); } catch { }
                    return dir;
                }
            }

            private static string GetTilePath(int z, int x, int y)
            {
                return Path.Combine(TileCacheDirectory, z.ToString() + "_" + x.ToString() + "_" + y.ToString() + ".png");
            }

            private void StartTileDownload(int z, int x, int y, string key, string path)
            {
                lock (_tileLock)
                {
                    if (_tileDownloads.Contains(key)) return;
                    _tileDownloads.Add(key);
                }

                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        try { System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072; } catch { }
                        string tmp = path + ".tmp";
                        string url = "https://tile.openstreetmap.org/" + z.ToString() + "/" + x.ToString() + "/" + y.ToString() + ".png";
                        using (System.Net.WebClient wc = new System.Net.WebClient())
                        {
                            wc.Headers.Add("User-Agent", "DXLogKSTBridge/1.0 (ham radio contest logger map)");
                            wc.DownloadFile(url, tmp);
                        }
                        if (File.Exists(path)) try { File.Delete(path); } catch { }
                        File.Move(tmp, path);
                    }
                    catch { }
                    finally
                    {
                        lock (_tileLock) _tileDownloads.Remove(key);
                        try { if (!IsDisposed) BeginInvoke((Action)(delegate { SceneChanged(); })); } catch { }
                    }
                });
            }

            private PointF Project(Rectangle area, double lat, double lon)
            {
                PointF center = LatLonToPixel(_centerLat, _centerLon, _tileZoom);
                PointF p = LatLonToPixel(lat, lon, _tileZoom);
                double mapSize = TileSize * (1 << _tileZoom);
                while (p.X - center.X > mapSize / 2.0) p.X -= (float)mapSize;
                while (p.X - center.X < -mapSize / 2.0) p.X += (float)mapSize;
                return new PointF((float)(area.Left + area.Width / 2.0 + (p.X - center.X)),
                                  (float)(area.Top + area.Height / 2.0 + (p.Y - center.Y)));
            }

            private static PointF LatLonToPixel(double lat, double lon, int zoom)
            {
                lat = Clamp(lat, -85.05112878, 85.05112878);
                lon = NormalizeLon(lon);
                double sinLat = Math.Sin(DegToRad(lat));
                double n = Math.Pow(2.0, zoom) * TileSize;
                double x = (lon + 180.0) / 360.0 * n;
                double y = (0.5 - Math.Log((1.0 + sinLat) / (1.0 - sinLat)) / (4.0 * Math.PI)) * n;
                return new PointF((float)x, (float)y);
            }

            private static GeoPoint PixelToLatLon(double x, double y, int zoom)
            {
                double n = Math.Pow(2.0, zoom) * TileSize;
                double lon = x / n * 360.0 - 180.0;
                double mercY = Math.PI * (1.0 - 2.0 * y / n);
                double lat = RadToDeg(Math.Atan(Math.Sinh(mercY)));
                return new GeoPoint { Lat = Clamp(lat, -85.05112878, 85.05112878), Lon = NormalizeLon(lon) };
            }

            private static double NormalizeLon(double lon)
            {
                while (lon < -180.0) lon += 360.0;
                while (lon > 180.0) lon -= 360.0;
                return lon;
            }

            private static double Clamp(double v, double min, double max)
            {
                if (v < min) return min;
                if (v > max) return max;
                return v;
            }

            private void DrawGrid(Graphics g, Rectangle area)
            {
                using (Pen p = new Pen(Color.FromArgb(100, 120, 155, 170)))
                using (Font f = new Font(_owner._windowFont.FontFamily, Math.Max(7, _owner._windowFont.Size - 1)))
                using (SolidBrush txt = new SolidBrush(Color.FromArgb(150, Color.DimGray)))
                {
                    for (double lon = -180; lon <= 180; lon += 10)
                    {
                        PointF a = Project(area, -80, lon);
                        PointF b = Project(area, 80, lon);
                        if ((a.X < area.Left && b.X < area.Left) || (a.X > area.Right && b.X > area.Right)) continue;
                        g.DrawLine(p, a, b);
                    }
                    for (double lat = -80; lat <= 80; lat += 5)
                    {
                        PointF a = Project(area, lat, -180);
                        PointF b = Project(area, lat, 180);
                        g.DrawLine(p, a, b);
                        if (a.Y >= area.Top && a.Y <= area.Bottom)
                            g.DrawString(lat.ToString("0") + "°", f, txt, area.Left + 3, a.Y + 1);
                    }
                }
            }

            private void DrawRangeRings(Graphics g, Rectangle area)
            {
                if (OwnStation == null) return;
                int[] rings = new int[] { 250, 500, 1000, 1500, 2000, 2500 };
                using (Pen p = new Pen(Color.FromArgb(150, 80, 140, 220)))
                using (Font f = new Font(_owner._windowFont.FontFamily, Math.Max(7, _owner._windowFont.Size - 1), FontStyle.Bold))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(180, Color.Navy)))
                {
                    foreach (int km in rings)
                    {
                        List<PointF> pts = new List<PointF>();
                        for (int brg = 0; brg <= 360; brg += 4)
                        {
                            GeoPoint gp = DestinationPoint(new GeoPoint { Lat = OwnStation.Lat, Lon = OwnStation.Lon }, brg, km);
                            PointF pt = Project(area, gp.Lat, gp.Lon);
                            if (pt.X < area.Left - 100 || pt.X > area.Right + 100 || pt.Y < area.Top - 100 || pt.Y > area.Bottom + 100) continue;
                            pts.Add(pt);
                        }
                        if (pts.Count > 2) g.DrawLines(p, pts.ToArray());
                        if (pts.Count > 0) g.DrawString(km.ToString() + " km", f, b, pts[0]);
                    }
                }
            }

            private static GeoPoint DestinationPoint(GeoPoint start, double bearingDeg, double distanceKm)
            {
                double r = distanceKm / 6371.0;
                double brg = DegToRad(bearingDeg);
                double lat1 = DegToRad(start.Lat);
                double lon1 = DegToRad(start.Lon);
                double lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(r) + Math.Cos(lat1) * Math.Sin(r) * Math.Cos(brg));
                double lon2 = lon1 + Math.Atan2(Math.Sin(brg) * Math.Sin(r) * Math.Cos(lat1), Math.Cos(r) - Math.Sin(lat1) * Math.Sin(lat2));
                return new GeoPoint { Lat = RadToDeg(lat2), Lon = NormalizeLon(RadToDeg(lon2)) };
            }


            private void DrawSelectedPath(Graphics g, Rectangle area)
            {
                if (OwnStation == null || SelectedStation == null) return;
                List<PointF> points = new List<PointF>();
                GeoPoint a = new GeoPoint { Lat = OwnStation.Lat, Lon = OwnStation.Lon };
                GeoPoint b = new GeoPoint { Lat = SelectedStation.Lat, Lon = SelectedStation.Lon };
                for (int i = 0; i <= 72; i++)
                {
                    GeoPoint gp = GreatCircleInterpolate(a, b, i / 72.0);
                    points.Add(Project(area, gp.Lat, gp.Lon));
                }
                if (points.Count < 2) return;

                using (Pen outline = new Pen(Color.FromArgb(210, Color.White), 5))
                using (Pen path = new Pen(Color.FromArgb(235, 20, 90, 205), 2.5f))
                {
                    outline.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                    path.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                    g.DrawLines(outline, points.ToArray());
                    g.DrawLines(path, points.ToArray());
                }

                PointF mid = points[points.Count / 2];
                string caption = OwnStation.Call + " - " + SelectedStation.Call + "  " + SelectedStation.Qrb + "  " + SelectedStation.Qtf;
                using (Font f = new Font(_owner._windowFont.FontFamily, Math.Max(8, _owner._windowFont.Size), FontStyle.Bold))
                {
                    SizeF captionSize = g.MeasureString(caption, f);
                    RectangleF[] candidates = new RectangleF[]
                    {
                        GetLabelRectangle(g, f, caption, mid.X + 10, mid.Y + 8),
                        GetLabelRectangle(g, f, caption, mid.X - captionSize.Width - 16, mid.Y + 8),
                        GetLabelRectangle(g, f, caption, mid.X + 10, mid.Y - captionSize.Height - 10),
                        GetLabelRectangle(g, f, caption, mid.X - captionSize.Width - 16, mid.Y - captionSize.Height - 10)
                    };
                    RectangleF chosen = ChooseFreeLabelRectangle(area, candidates, _staticLabelBounds);
                    if (chosen.IsEmpty) chosen = ChooseLeastOverlappingLabelRectangle(area, candidates, _staticLabelBounds);
                    if (!chosen.IsEmpty)
                    {
                        DrawLabel(g, f, caption, chosen.X, chosen.Y, Color.Navy, Color.FromArgb(225, Color.White));
                        _staticLabelBounds.Add(chosen);
                    }
                }
            }

            private static GeoPoint GreatCircleInterpolate(GeoPoint a, GeoPoint b, double fraction)
            {
                fraction = Clamp(fraction, 0, 1);
                double lat1 = DegToRad(a.Lat);
                double lon1 = DegToRad(a.Lon);
                double lat2 = DegToRad(b.Lat);
                double lon2 = DegToRad(b.Lon);
                double cosD = Math.Sin(lat1) * Math.Sin(lat2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Cos(lon2 - lon1);
                cosD = Clamp(cosD, -1, 1);
                double d = Math.Acos(cosD);
                if (d < 1e-9) return a;
                double sinD = Math.Sin(d);
                double wa = Math.Sin((1 - fraction) * d) / sinD;
                double wb = Math.Sin(fraction * d) / sinD;
                double x = wa * Math.Cos(lat1) * Math.Cos(lon1) + wb * Math.Cos(lat2) * Math.Cos(lon2);
                double y = wa * Math.Cos(lat1) * Math.Sin(lon1) + wb * Math.Cos(lat2) * Math.Sin(lon2);
                double z = wa * Math.Sin(lat1) + wb * Math.Sin(lat2);
                return new GeoPoint { Lat = RadToDeg(Math.Atan2(z, Math.Sqrt(x * x + y * y))), Lon = NormalizeLon(RadToDeg(Math.Atan2(y, x))) };
            }

            private void DrawAircraft(Graphics g, Rectangle area)
            {
                _hits.RemoveAll(delegate(KstMapHit hit) { return hit != null && hit.Aircraft != null; });
                if (Aircraft == null || Aircraft.Count == 0) return;
                List<RectangleF> occupied = new List<RectangleF>(_staticLabelBounds ?? new List<RectangleF>());
                using (Font labelFont = new Font(_owner._windowFont.FontFamily, Math.Max(7, _owner._windowFont.Size - 1), FontStyle.Bold))
                {
                    foreach (KstMapAircraft plane in Aircraft)
                    {
                        if (plane == null) continue;
                        PointF pt = Project(area, plane.Lat, plane.Lon);
                        if (pt.X < area.Left - 100 || pt.X > area.Right + 100 || pt.Y < area.Top - 60 || pt.Y > area.Bottom + 60) continue;
                        Color colour = plane.Mins <= 0 ? Color.LimeGreen : Color.Orange;
                        if (ShowAircraftTrails)
                        {
                            string trailKey = CleanCall(plane.Call);
                            List<KstAircraftTrailPoint> trail;
                            if (!String.IsNullOrWhiteSpace(trailKey) && _aircraftTrails.TryGetValue(trailKey, out trail) && trail != null && trail.Count > 1)
                            {
                                List<PointF> trailPixels = new List<PointF>();
                                foreach (KstAircraftTrailPoint trailPoint in trail)
                                    if (trailPoint != null) trailPixels.Add(Project(area, trailPoint.Lat, trailPoint.Lon));
                                if (trailPixels.Count > 1)
                                {
                                    using (Pen trailHalo = new Pen(Color.FromArgb(120, Color.White), 4f)) g.DrawLines(trailHalo, trailPixels.ToArray());
                                    using (Pen trailPen = new Pen(Color.FromArgb(180, colour), 2f)) g.DrawLines(trailPen, trailPixels.ToArray());
                                }
                            }
                        }
                        DrawAircraftSymbol(g, pt, plane.Track, colour);
                        _hits.Add(new KstMapHit { Aircraft = plane, Bounds = new RectangleF(pt.X - 10, pt.Y - 10, 20, 20) });
                        string when = plane.Mins <= 0 ? "NOW" : plane.Mins.ToString() + "m";
                        string label = (String.IsNullOrWhiteSpace(plane.Call) ? "AIRCRAFT" : plane.Call.Trim()) + " " + when;
                        if (!String.IsNullOrWhiteSpace(plane.Size)) label += " " + plane.Size;
                        if (plane.AltitudeFt > 0) label += " " + Math.Round(plane.AltitudeFt / 1000.0, 0).ToString("0") + "kft";

                        SizeF labelSize = g.MeasureString(label, labelFont);
                        RectangleF[] candidates = new RectangleF[]
                        {
                            GetLabelRectangle(g, labelFont, label, pt.X + 12, pt.Y - 14),
                            GetLabelRectangle(g, labelFont, label, pt.X - labelSize.Width - 18, pt.Y - 14),
                            GetLabelRectangle(g, labelFont, label, pt.X + 12, pt.Y + 8),
                            GetLabelRectangle(g, labelFont, label, pt.X - labelSize.Width - 18, pt.Y + 8),
                            GetLabelRectangle(g, labelFont, label, pt.X - labelSize.Width / 2 - 3, pt.Y - labelSize.Height - 16),
                            GetLabelRectangle(g, labelFont, label, pt.X - labelSize.Width / 2 - 3, pt.Y + 14),
                            GetLabelRectangle(g, labelFont, label, pt.X + 22, pt.Y - labelSize.Height - 20),
                            GetLabelRectangle(g, labelFont, label, pt.X - labelSize.Width - 28, pt.Y - labelSize.Height - 20)
                        };
                        RectangleF chosen = ChooseFreeLabelRectangle(area, candidates, occupied);
                        // In a very crowded area keep the aircraft symbol visible rather
                        // than painting a label over a station, path or another aircraft.
                        if (!chosen.IsEmpty)
                        {
                            DrawLabel(g, labelFont, label, chosen.X, chosen.Y, Color.Black, Color.FromArgb(235, colour));
                            occupied.Add(chosen);
                        }
                    }
                }
            }

            private static void DrawAircraftSymbol(Graphics g, PointF center, double track, Color colour)
            {
                double r = DegToRad(track);
                PointF forward = new PointF((float)Math.Sin(r), (float)-Math.Cos(r));
                PointF right = new PointF(-forward.Y, forward.X);
                PointF nose = new PointF(center.X + forward.X * 10, center.Y + forward.Y * 10);
                PointF leftWing = new PointF(center.X - forward.X * 3 - right.X * 7, center.Y - forward.Y * 3 - right.Y * 7);
                PointF tail = new PointF(center.X - forward.X * 8, center.Y - forward.Y * 8);
                PointF rightWing = new PointF(center.X - forward.X * 3 + right.X * 7, center.Y - forward.Y * 3 + right.Y * 7);
                PointF[] shape = new PointF[] { nose, leftWing, center, tail, center, rightWing };
                using (Pen halo = new Pen(Color.White, 5))
                using (Pen pen = new Pen(colour, 3))
                {
                    halo.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                    pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                    g.DrawLines(halo, shape);
                    g.DrawLines(pen, shape);
                }
                using (SolidBrush b = new SolidBrush(colour)) g.FillEllipse(b, center.X - 3, center.Y - 3, 6, 6);
                using (Pen p = new Pen(Color.Black)) g.DrawEllipse(p, center.X - 3, center.Y - 3, 6, 6);
            }

            private void DrawStations(Graphics g, Rectangle area)
            {
                _hits = new List<KstMapHit>();
                List<RectangleF> occupied = new List<RectangleF>(_staticLabelBounds ?? new List<RectangleF>());
                using (Font labelFont = new Font(_owner._windowFont.FontFamily, Math.Max(7, _owner._windowFont.Size - 1), FontStyle.Bold))
                using (Font ownFont = new Font(_owner._windowFont.FontFamily, Math.Max(8, _owner._windowFont.Size), FontStyle.Bold))
                {
                    if (OwnStation != null)
                    {
                        PointF own = Project(area, OwnStation.Lat, OwnStation.Lon);
                        using (SolidBrush b = new SolidBrush(Color.LimeGreen)) g.FillEllipse(b, own.X - 6, own.Y - 6, 12, 12);
                        using (Pen p = new Pen(Color.DarkGreen, 2)) g.DrawEllipse(p, own.X - 6, own.Y - 6, 12, 12);
                        RectangleF ownLabel = GetLabelRectangle(g, ownFont, OwnStation.Call, own.X + 8, own.Y - 14);
                        DrawLabel(g, ownFont, OwnStation.Call, ownLabel.X, ownLabel.Y, Color.DarkGreen, Color.FromArgb(220, Color.White));
                        occupied.Add(ownLabel);
                    }

                    List<KstMapStation> ordered = new List<KstMapStation>(Stations ?? new List<KstMapStation>());
                    ordered.Sort(delegate(KstMapStation a, KstMapStation b)
                    {
                        if (a == null && b == null) return 0;
                        if (a == null) return 1;
                        if (b == null) return -1;
                        if (a.IsSelected != b.IsSelected) return a.IsSelected ? -1 : 1;
                        if (a.IsWatched != b.IsWatched) return a.IsWatched ? -1 : 1;
                        if (a.IsActive != b.IsActive) return a.IsActive ? -1 : 1;
                        if (a.Worked != b.Worked) return a.Worked ? 1 : -1;
                        return String.Compare(a.Call, b.Call, StringComparison.OrdinalIgnoreCase);
                    });

                    foreach (KstMapStation station in ordered)
                    {
                        KstMapStation s = station;
                        if (s == null) continue;
                        PointF pt = Project(area, s.Lat, s.Lon);
                        if (pt.X < area.Left - 20 || pt.X > area.Right + 20 || pt.Y < area.Top - 20 || pt.Y > area.Bottom + 20) continue;
                        Color dot;
                        Color outline;
                        Color labelBack;
                        Color labelText = s.Worked ? Color.DimGray : Color.Black;
                        if (s.IsSelected)
                        {
                            dot = Color.Red;
                            outline = Color.DarkRed;
                            labelBack = Color.FromArgb(245, Color.LightCyan);
                        }
                        else if (s.IsActive)
                        {
                            dot = s.Worked ? Color.DarkSeaGreen : Color.LimeGreen;
                            outline = Color.DarkGreen;
                            labelBack = s.Worked ? Color.FromArgb(225, Color.Honeydew) : Color.FromArgb(235, Color.LightGreen);
                        }
                        else
                        {
                            dot = s.Worked ? Color.Khaki : Color.Gold;
                            outline = Color.DarkGoldenrod;
                            labelBack = s.Worked ? Color.FromArgb(225, Color.LightYellow) : Color.FromArgb(235, Color.Yellow);
                        }
                        using (SolidBrush b = new SolidBrush(dot)) g.FillEllipse(b, pt.X - 4, pt.Y - 4, 8, 8);
                        using (Pen p = new Pen(outline, s.IsWatched ? 2.2f : 1f)) g.DrawEllipse(p, pt.X - 4, pt.Y - 4, 8, 8);
                        _hits.Add(new KstMapHit { Station = s, Bounds = new RectangleF(pt.X - 8, pt.Y - 8, 16, 16) });

                        string stationLabel = s.IsWatched ? "★ " + s.Call : s.Call;
                        SizeF stationLabelSize = g.MeasureString(stationLabel, labelFont);
                        RectangleF[] candidates = new RectangleF[]
                        {
                            GetLabelRectangle(g, labelFont, stationLabel, pt.X + 7, pt.Y - 10),
                            GetLabelRectangle(g, labelFont, stationLabel, pt.X - stationLabelSize.Width - 13, pt.Y - 10),
                            GetLabelRectangle(g, labelFont, stationLabel, pt.X + 7, pt.Y + 6),
                            GetLabelRectangle(g, labelFont, stationLabel, pt.X - stationLabelSize.Width - 13, pt.Y + 6),
                            GetLabelRectangle(g, labelFont, stationLabel, pt.X - stationLabelSize.Width / 2 - 3, pt.Y - stationLabelSize.Height - 9),
                            GetLabelRectangle(g, labelFont, stationLabel, pt.X - stationLabelSize.Width / 2 - 3, pt.Y + 10)
                        };

                        RectangleF chosen = ChooseFreeLabelRectangle(area, candidates, occupied);

                        // Selected stations retain a label, using the least-overlapping
                        // legal position if the immediate area is unusually crowded.
                        if (chosen.IsEmpty && s.IsSelected)
                            chosen = ChooseLeastOverlappingLabelRectangle(area, candidates, occupied);
                        if (!chosen.IsEmpty)
                        {
                            DrawLabel(g, labelFont, stationLabel, chosen.X, chosen.Y, labelText, labelBack);
                            occupied.Add(chosen);
                        }
                    }
                    _staticLabelBounds = occupied;
                }
            }

            private static RectangleF ChooseFreeLabelRectangle(Rectangle area, RectangleF[] candidates, List<RectangleF> occupied)
            {
                if (candidates == null) return RectangleF.Empty;
                foreach (RectangleF candidate in candidates)
                {
                    if (!LabelFitsArea(area, candidate)) continue;
                    if (!LabelIntersects(candidate, occupied)) return candidate;
                }
                return RectangleF.Empty;
            }

            private static RectangleF ChooseLeastOverlappingLabelRectangle(Rectangle area, RectangleF[] candidates, List<RectangleF> occupied)
            {
                RectangleF best = RectangleF.Empty;
                float bestOverlap = Single.MaxValue;
                if (candidates == null) return best;
                foreach (RectangleF candidate in candidates)
                {
                    if (!LabelFitsArea(area, candidate)) continue;
                    float overlap = 0;
                    if (occupied != null)
                    {
                        foreach (RectangleF used in occupied)
                        {
                            RectangleF expanded = used;
                            expanded.Inflate(3, 2);
                            RectangleF intersection = RectangleF.Intersect(expanded, candidate);
                            if (!intersection.IsEmpty) overlap += intersection.Width * intersection.Height;
                        }
                    }
                    if (overlap < bestOverlap)
                    {
                        bestOverlap = overlap;
                        best = candidate;
                    }
                }
                return best;
            }

            private static bool LabelFitsArea(Rectangle area, RectangleF candidate)
            {
                return candidate.Left >= area.Left + 1 && candidate.Top >= area.Top + 1 &&
                       candidate.Right <= area.Right - 1 && candidate.Bottom <= area.Bottom - 1;
            }

            private static bool LabelIntersects(RectangleF candidate, List<RectangleF> occupied)
            {
                if (occupied == null) return false;
                foreach (RectangleF used in occupied)
                {
                    RectangleF expanded = used;
                    expanded.Inflate(3, 2);
                    if (expanded.IntersectsWith(candidate)) return true;
                }
                return false;
            }

            private static RectangleF GetLabelRectangle(Graphics g, Font font, string text, float x, float y)
            {
                SizeF size = g.MeasureString(text ?? "", font);
                return new RectangleF(x, y, size.Width + 6, size.Height + 2);
            }

            private static void DrawLabel(Graphics g, Font font, string text, float x, float y, Color fore, Color back)
            {
                SizeF size = g.MeasureString(text, font);
                RectangleF r = new RectangleF(x, y, size.Width + 6, size.Height + 2);
                using (SolidBrush b = new SolidBrush(back)) g.FillRectangle(b, r);
                using (Pen p = new Pen(Color.Goldenrod)) g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
                using (SolidBrush b = new SolidBrush(fore)) g.DrawString(text, font, b, x + 3, y + 1);
            }

            private void CanvasMouseDown(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                // The post-drag frame swap normally completes in about 160 ms.
                // Ignore a second drag during that tiny interval rather than mixing
                // coordinates from the old cached image and the new map centre.
                if (_panCommitPending) return;
                _dragging = true;
                _dragMoved = false;
                _dragStart = e.Location;
                PointF center = LatLonToPixel(_centerLat, _centerLon, _tileZoom);
                _dragStartCenterX = center.X;
                _dragStartCenterY = center.Y;
                Capture = true;
                Cursor = Cursors.SizeAll;
            }

            private void CanvasMouseMove(object sender, MouseEventArgs e)
            {
                if (!_dragging)
                {
                    KstMapHit hover = FindHit(e.Location);
                    string hoverText = GetHoverText(hover);
                    if (!String.Equals(hoverText, _lastHoverText, StringComparison.Ordinal))
                    {
                        _lastHoverText = hoverText;
                        Action<string> handler = HoverTextChanged;
                        if (handler != null) handler(hoverText);
                    }
                    return;
                }
                int dx = e.X - _dragStart.X;
                int dy = e.Y - _dragStart.Y;
                if (Math.Abs(dx) > 2 || Math.Abs(dy) > 2) _dragMoved = true;
                if (!_dragMoved) return;
                _dragOffset = new Point(dx, dy);
                Invalidate();
            }

            public void ShowStationContextMenu(Point location)
            {
                KstMapHit hit = FindHit(location);
                if (hit == null || hit.Station == null || String.IsNullOrWhiteSpace(hit.Station.Call)) return;
                ContextMenuStrip menu = _owner.MakeCallMenu(hit.Station.Call);
                menu.Show(this, location);
            }

            private void CanvasMouseUp(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right)
                {
                    ShowStationContextMenu(e.Location);
                    return;
                }

                if (e.Button != MouseButtons.Left) return;
                bool click = _dragging && !_dragMoved;
                Point offset = _dragOffset;
                _dragging = false;
                Capture = false;
                Cursor = Cursors.Hand;
                if (click)
                {
                    _dragOffset = Point.Empty;
                    _panCommitPending = false;
                    KstMapHit best = FindHit(e.Location);
                    if (best != null && best.Station != null && StationClicked != null) StationClicked(best.Station);
                }
                else
                {
                    // Keep the old frame at its final dragged offset while the new-centre
                    // frame is built. Clearing the offset here caused a visible snap back
                    // to the previous position for the 160 ms rebuild delay.
                    _panCommitPending = true;
                    GeoPoint gp = PixelToLatLon(_dragStartCenterX - offset.X, _dragStartCenterY - offset.Y, _tileZoom);
                    _centerLat = gp.Lat;
                    _centerLon = gp.Lon;
                    _fitPending = false;
                    SceneChanged();
                }
            }

            private static string GetHoverText(KstMapHit hit)
            {
                if (hit == null) return "";
                if (hit.Aircraft != null)
                {
                    KstMapAircraft plane = hit.Aircraft;
                    StringBuilder text = new StringBuilder();
                    text.Append(String.IsNullOrWhiteSpace(plane.Flight) ? plane.Call : plane.Flight);
                    if (!String.IsNullOrWhiteSpace(plane.Registration)) text.AppendLine().Append(plane.Registration);
                    if (!String.IsNullOrWhiteSpace(plane.Model)) text.AppendLine().Append(plane.Model);
                    if (!String.IsNullOrWhiteSpace(plane.Type)) text.AppendLine().Append("Type: ").Append(plane.Type);
                    if (!String.IsNullOrWhiteSpace(plane.Size)) text.AppendLine().Append("Size: ").Append(plane.Size);
                    if (!String.IsNullOrWhiteSpace(plane.TrafficClass)) text.AppendLine().Append("Category: ").Append(plane.TrafficClass).Append(" (classified)");
                    if (plane.AltitudeFt > 0) text.AppendLine().Append("Altitude: ").Append(plane.AltitudeFt.ToString("N0")).Append(" ft");
                    if (plane.SpeedKt > 0) text.AppendLine().Append("Speed: ").Append(plane.SpeedKt.ToString()).Append(" kt");
                    text.AppendLine().Append("Track: ").Append(Math.Round(plane.Track, 0).ToString("000")).Append("°");
                    text.AppendLine().Append("Opportunity: ").Append(plane.Mins <= 0 ? "NOW" : plane.Mins.ToString() + " min");
                    return text.ToString();
                }
                if (hit.Station != null)
                {
                    KstMapStation station = hit.Station;
                    string text = station.Call;
                    if (!String.IsNullOrWhiteSpace(station.Name)) text += " - " + station.Name;
                    if (!String.IsNullOrWhiteSpace(station.Locator)) text += Environment.NewLine + station.Locator;
                    return text;
                }
                return "";
            }

            private KstMapHit FindHit(Point pnt)
            {
                KstMapHit best = null;
                double bestDist = 99999;
                foreach (KstMapHit h in _hits)
                {
                    RectangleF inflated = h.Bounds;
                    inflated.Inflate(12, 12);
                    if (!inflated.Contains(pnt)) continue;
                    double cx = h.Bounds.Left + h.Bounds.Width / 2.0;
                    double cy = h.Bounds.Top + h.Bounds.Height / 2.0;
                    double d = Math.Sqrt((cx - pnt.X) * (cx - pnt.X) + (cy - pnt.Y) * (cy - pnt.Y));
                    if (d < bestDist) { bestDist = d; best = h; }
                }
                return best;
            }
        }

        private sealed class KstAircraftTrailPoint
        {
            public double Lat;
            public double Lon;
            public DateTime Utc;
        }

        private sealed class KstMapHit
        {
            public KstMapStation Station;
            public KstMapAircraft Aircraft;
            public RectangleF Bounds;
        }

        private static string CleanCall(string call)
        {
            if (String.IsNullOrWhiteSpace(call)) return "";
            call = call.Trim().ToUpperInvariant();
            if (call.StartsWith("(") && call.EndsWith(")") && call.Length > 2) call = call.Substring(1, call.Length - 2);
            return Regex.Replace(call, "[^A-Z0-9/]+", "");
        }
    }


    internal sealed class BridgeContextMenuMessageFilter : IMessageFilter
    {
        private const int WM_CONTEXTMENU = 0x007B;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_NCRBUTTONUP = 0x00A5;
        private readonly WeakReference _ownerReference;
        private DateTime _suppressContextMenuUntilUtc = DateTime.MinValue;

        public BridgeContextMenuMessageFilter(KstChatBridge owner)
        {
            _ownerReference = new WeakReference(owner);
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_CONTEXTMENU && m.Msg != WM_RBUTTONDOWN &&
                m.Msg != WM_RBUTTONUP && m.Msg != WM_NCRBUTTONUP) return false;

            KstChatBridge owner = _ownerReference.Target as KstChatBridge;
            if (owner == null || owner.IsDisposed || !owner.OwnsContextMenuHandle(m.HWnd)) return false;

            // Consume the button-down message before DXLog's host window sees it.
            // Selection and the correct bridge menu are handled on button-up.
            if (m.Msg == WM_RBUTTONDOWN) return true;

            Point screenPoint = GetScreenPoint(m);

            if (m.Msg == WM_RBUTTONUP || m.Msg == WM_NCRBUTTONUP)
            {
                owner.HandleBridgeRightClick(m.HWnd, screenPoint);
                _suppressContextMenuUntilUtc = DateTime.UtcNow.AddMilliseconds(600);
                return true;
            }

            // Windows may emit WM_CONTEXTMENU after WM_RBUTTONUP.  The menu was
            // already shown above, so consume the follow-up without displaying it
            // a second time.  Keyboard context-menu requests still work normally.
            if (DateTime.UtcNow >= _suppressContextMenuUntilUtc)
                owner.HandleBridgeRightClick(m.HWnd, screenPoint);
            return true;
        }

        private static Point GetScreenPoint(Message m)
        {
            if (m.Msg == WM_RBUTTONDOWN || m.Msg == WM_RBUTTONUP || m.Msg == WM_NCRBUTTONUP)
                return Cursor.Position;

            long raw = m.LParam.ToInt64();
            if (raw == -1) return Cursor.Position;
            int x = unchecked((short)(raw & 0xFFFF));
            int y = unchecked((short)((raw >> 16) & 0xFFFF));
            return new Point(x, y);
        }
    }

    internal sealed class ComposeInputMessageFilter : IMessageFilter
    {
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_CHAR = 0x0102;
        private const int WM_DEADCHAR = 0x0103;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int WM_SYSCHAR = 0x0106;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;

        private readonly Func<bool> _shouldCapture;
        private readonly Func<IntPtr> _targetHandle;
        private readonly Action _release;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public ComposeInputMessageFilter(Func<bool> shouldCapture, Func<IntPtr> targetHandle, Action release)
        {
            _shouldCapture = shouldCapture;
            _targetHandle = targetHandle;
            _release = release;
        }

        public bool PreFilterMessage(ref Message m)
        {
            bool capture = false;
            try { capture = _shouldCapture != null && _shouldCapture(); } catch { }
            if (!capture) return false;

            IntPtr target = IntPtr.Zero;
            try { if (_targetHandle != null) target = _targetHandle(); } catch { }
            if (target == IntPtr.Zero) return false;

            if (m.Msg == WM_LBUTTONDOWN || m.Msg == WM_RBUTTONDOWN || m.Msg == WM_MBUTTONDOWN)
            {
                if (m.HWnd != target)
                {
                    try { if (_release != null) _release(); } catch { }
                }
                return false;
            }

            if (m.Msg == WM_KEYDOWN && (Keys)(int)m.WParam == Keys.Escape)
            {
                try { if (_release != null) _release(); } catch { }
                return true;
            }

            if (m.Msg == WM_KEYDOWN && (Keys)(int)m.WParam == Keys.Tab)
            {
                try { if (_release != null) _release(); } catch { }
                return false;
            }

            if (m.Msg == WM_KEYDOWN || m.Msg == WM_KEYUP || m.Msg == WM_CHAR || m.Msg == WM_DEADCHAR ||
                m.Msg == WM_SYSKEYDOWN || m.Msg == WM_SYSKEYUP || m.Msg == WM_SYSCHAR)
            {
                // Once a redirected message reaches the compose TextBox, let the
                // normal WinForms message loop translate and dispatch it. This is
                // important because printable WM_KEYDOWN messages must be translated
                // into WM_CHAR by Windows before the native edit control inserts text.
                if (m.HWnd == target) return false;

                // DXLog can route the physical key to its QSO entry line even after
                // the compose TextBox was selected. Re-post the key to our TextBox
                // and consume the original. The posted key then follows the normal
                // TranslateMessage/DispatchMessage path, including Shift, punctuation,
                // keyboard layouts, Backspace, cursor keys and clipboard shortcuts.
                try { PostMessage(target, m.Msg, m.WParam, m.LParam); } catch { }
                return true;
            }
            return false;
        }
    }

    internal sealed class AirScoutLivePlane
    {
        public string Hex;
        public string Call;
        public string Flight;
        public string Registration;
        public string Type;
        public double Lat;
        public double Lon;
        public double Track;
        public int AltitudeFt;
        public int SpeedKt;
        public long ReportedUnix;

        public string DisplayName
        {
            get
            {
                if (!String.IsNullOrWhiteSpace(Call)) return Call.Trim();
                if (!String.IsNullOrWhiteSpace(Flight)) return Flight.Trim();
                if (!String.IsNullOrWhiteSpace(Registration)) return Registration.Trim();
                return Hex ?? "";
            }
        }
    }

    internal sealed class AirScoutPlaneInfo
    {
        public string Call;
        public string Category;
        public int IntQRB;
        public int Potential;
        public int Mins;
    }

    internal sealed class AirScoutPathResult
    {
        public string DxCall = "";
        public DateTime UpdatedUtc = DateTime.UtcNow;
        public readonly List<AirScoutPlaneInfo> Planes = new List<AirScoutPlaneInfo>();

        public AirScoutPlaneInfo GetBestPlane()
        {
            AirScoutPlaneInfo best = null;
            foreach (AirScoutPlaneInfo plane in Planes)
            {
                if (plane == null || plane.Mins >= 30) continue;
                if (best == null || plane.Potential > best.Potential ||
                    (plane.Potential == best.Potential && plane.IntQRB < best.IntQRB))
                    best = plane;
            }
            return best;
        }
    }

    internal sealed class AirScoutClient : IDisposable
    {
        private readonly int _port;
        private readonly string _sourceName;
        private readonly string _serverName;
        private UdpClient _listener;
        private CancellationTokenSource _cancel;
        private Task _receiveTask;

        public bool IsListening { get; private set; }
        public event Action<AirScoutPathResult> PathResultReceived;
        public event Action<string> StatusChanged;

        public AirScoutClient(int port, string sourceName, string serverName)
        {
            _port = port > 0 && port <= 65535 ? port : 9872;
            _sourceName = String.IsNullOrWhiteSpace(sourceName) ? "KST" : sourceName.Trim();
            _serverName = String.IsNullOrWhiteSpace(serverName) ? "AS" : serverName.Trim();
        }

        public void Start()
        {
            if (IsListening) return;
            try
            {
                _cancel = new CancellationTokenSource();
                _listener = new UdpClient();
                _listener.ExclusiveAddressUse = false;
                _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Client.Bind(new IPEndPoint(IPAddress.Any, _port));
                IsListening = true;
                RaiseStatus("Listening on UDP " + _port.ToString());
                CancellationToken token = _cancel.Token;
                _receiveTask = Task.Run(() => ReceiveLoopAsync(token));
            }
            catch (Exception ex)
            {
                IsListening = false;
                RaiseStatus("Error: " + ex.Message);
                DisposeSocketOnly();
            }
        }

        public void SendSetPath(string data)
        {
            Send("ASSETPATH", data);
        }

        public void SendShowPath(string data)
        {
            Send("ASSHOWPATH", data);
        }

        private void Send(string command, string data)
        {
            if (!IsListening) return;
            byte[] packet = BuildPacket(command, _sourceName, _serverName, data ?? "");
            using (UdpClient sender = new UdpClient())
            {
                sender.EnableBroadcast = true;
                sender.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, _port));
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    UdpReceiveResult received = await _listener.ReceiveAsync().ConfigureAwait(false);
                    if (token.IsCancellationRequested) break;
                    AirScoutPathResult result;
                    if (TryParseNearest(received.Buffer, out result))
                    {
                        Action<AirScoutPathResult> handler = PathResultReceived;
                        if (handler != null) handler(result);
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested) RaiseStatus("Error: " + ex.Message);
                    break;
                }
            }
            IsListening = false;
        }

        private static byte[] BuildPacket(string command, string source, string destination, string data)
        {
            // AirScout uses the legacy Win-Test UDP framing. For outgoing commands
            // the wire format is:
            //   COMMAND: "SRC" "DST" "DATA"?<checksum>
            // The final '?' is only a placeholder and is replaced by the checksum;
            // one literal '?' remains immediately before the checksum byte.
            string text = command + ": \"" + source + "\" \"" + destination + "\" \"" + (data ?? "") + "\"??";
            byte[] packet = Encoding.ASCII.GetBytes(text);
            byte sum = 0;
            for (int i = 0; i < packet.Length - 1; i++) sum += packet[i];
            packet[packet.Length - 1] = (byte)(sum | 0x80);
            return packet;
        }

        private static bool TryParseNearest(byte[] packet, out AirScoutPathResult result)
        {
            result = null;
            if (packet == null || packet.Length < 8) return false;

            int effectiveLength = packet.Length;
            // Be tolerant of an optional trailing NUL from third-party implementations,
            // although native AirScout packets end directly with the checksum byte.
            while (effectiveLength > 0 && packet[effectiveLength - 1] == 0) effectiveLength--;
            if (effectiveLength < 2) return false;
            int checksumIndex = effectiveLength - 1;
            byte sum = 0;
            for (int i = 0; i < checksumIndex; i++) sum += packet[i];
            byte expected = (byte)(sum | 0x80);
            byte actual = (byte)(packet[checksumIndex] | 0x80);
            if (actual != expected) return false;

            string text = Encoding.ASCII.GetString(packet, 0, checksumIndex).Trim();
            // Legacy command packets can leave one '?' immediately before the checksum.
            // ASNEAREST normally does not, but trimming it makes the parser robust.
            text = text.TrimEnd('?');
            string command;
            string source;
            string destination;
            string data;
            if (!TryParseMessageText(text, out command, out source, out destination, out data)) return false;
            if (!String.Equals(command, "ASNEAREST", StringComparison.OrdinalIgnoreCase)) return false;

            string[] parts = data.Split(',');
            if (parts.Length < 6) return false;
            int planeCount;
            if (!Int32.TryParse(parts[5].Trim(), out planeCount) || planeCount < 0) planeCount = 0;

            AirScoutPathResult parsed = new AirScoutPathResult();
            parsed.DxCall = parts[3].Trim();
            parsed.UpdatedUtc = DateTime.UtcNow;
            int available = Math.Min(planeCount, Math.Max(0, (parts.Length - 6) / 5));
            for (int i = 0; i < available; i++)
            {
                int offset = 6 + (i * 5);
                int intQrb;
                int potential;
                int mins;
                if (!Int32.TryParse(parts[offset + 2].Trim(), out intQrb)) intQrb = 0;
                if (!Int32.TryParse(parts[offset + 3].Trim(), out potential)) potential = 0;
                if (!Int32.TryParse(parts[offset + 4].Trim(), out mins)) mins = 0;
                parsed.Planes.Add(new AirScoutPlaneInfo
                {
                    Call = parts[offset].Trim(),
                    Category = parts[offset + 1].Trim(),
                    IntQRB = intQrb,
                    Potential = potential,
                    Mins = mins
                });
            }
            result = parsed;
            return !String.IsNullOrWhiteSpace(parsed.DxCall);
        }

        private static bool TryParseMessageText(string text, out string command, out string source, out string destination, out string data)
        {
            command = "";
            source = "";
            destination = "";
            data = "";
            if (String.IsNullOrWhiteSpace(text)) return false;
            int colon = text.IndexOf(':');
            if (colon <= 0) return false;
            command = text.Substring(0, colon).Trim();
            int pos = colon + 1;
            SkipSpaces(text, ref pos);
            if (!ReadQuoted(text, ref pos, out source)) return false;
            SkipSpaces(text, ref pos);
            if (!ReadQuoted(text, ref pos, out destination)) return false;
            SkipSpaces(text, ref pos);
            data = pos < text.Length ? text.Substring(pos).Trim() : "";
            if (data.Length >= 2 && data[0] == '"' && data[data.Length - 1] == '"')
                data = data.Substring(1, data.Length - 2);
            return true;
        }

        private static void SkipSpaces(string text, ref int pos)
        {
            while (pos < text.Length && Char.IsWhiteSpace(text[pos])) pos++;
        }

        private static bool ReadQuoted(string text, ref int pos, out string value)
        {
            value = "";
            if (pos >= text.Length || text[pos] != '"') return false;
            int start = ++pos;
            int end = text.IndexOf('"', start);
            if (end < 0) return false;
            value = text.Substring(start, end - start);
            pos = end + 1;
            return true;
        }

        private void RaiseStatus(string status)
        {
            Action<string> handler = StatusChanged;
            if (handler != null) handler(status);
        }

        private void DisposeSocketOnly()
        {
            try { if (_listener != null) _listener.Close(); } catch { }
            _listener = null;
        }

        public void Dispose()
        {
            try { if (_cancel != null) _cancel.Cancel(); } catch { }
            DisposeSocketOnly();
            IsListening = false;
            if (_cancel != null)
            {
                _cancel.Dispose();
                _cancel = null;
            }
            _receiveTask = null;
        }
    }

    internal sealed class ReferenceObjectComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceObjectComparer Instance = new ReferenceObjectComparer();

        private ReferenceObjectComparer() { }

        public new bool Equals(object x, object y)
        {
            return Object.ReferenceEquals(x, y);
        }

        public int GetHashCode(object obj)
        {
            return obj == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }

    internal sealed class KstUserListComparer : IComparer
    {
        private readonly int _column;
        private readonly SortOrder _order;

        public KstUserListComparer(int column, SortOrder order)
        {
            _column = column;
            _order = order;
        }

        public int Compare(object x, object y)
        {
            ListViewItem a = x as ListViewItem;
            ListViewItem b = y as ListViewItem;
            if (a == null || b == null) return 0;

            string av = GetSubItem(a, _column);
            string bv = GetSubItem(b, _column);
            KstUserInfo au = a.Tag as KstUserInfo;
            KstUserInfo bu = b.Tag as KstUserInfo;
            if (au != null && bu != null && au.IsWatched != bu.IsWatched)
                return au.IsWatched ? -1 : 1;
            int result;

            if (_column == 3 || _column == 4)
                result = CompareNumericWithBlanksLast(av, bv);
            else if (_column == 5)
                result = CompareAirScoutOpportunity(av, bv);
            else if (_column == 6)
                result = CompareLastActivity(a, b);
            else if (_column == 7)
                result = CompareNumericWithBlanksLast(av, bv);
            else if (_column >= 8)
                result = CompareWorkedBand(av, bv);
            else
                result = String.Compare(av, bv, StringComparison.OrdinalIgnoreCase);

            if (_order == SortOrder.Descending) result = -result;
            if (result == 0) result = String.Compare(GetSubItem(a, 0), GetSubItem(b, 0), StringComparison.OrdinalIgnoreCase);
            return result;
        }

        private static string GetSubItem(ListViewItem item, int column)
        {
            if (item == null || column < 0 || column >= item.SubItems.Count) return "";
            return item.SubItems[column].Text ?? "";
        }


        private static int CompareLastActivity(ListViewItem a, ListViewItem b)
        {
            KstUserInfo au = a == null ? null : a.Tag as KstUserInfo;
            KstUserInfo bu = b == null ? null : b.Tag as KstUserInfo;
            DateTime at = au == null ? DateTime.MinValue : au.LastActivityUtc;
            DateTime bt = bu == null ? DateTime.MinValue : bu.LastActivityUtc;
            if (at == DateTime.MinValue && bt == DateTime.MinValue) return 0;
            if (at == DateTime.MinValue) return 1;
            if (bt == DateTime.MinValue) return -1;
            return at.CompareTo(bt);
        }

        private static int CompareAirScoutOpportunity(string a, string b)
        {
            return AirScoutSortValue(a).CompareTo(AirScoutSortValue(b));
        }

        private static int CompareWorkedBand(string a, string b)
        {
            bool aw = !String.IsNullOrWhiteSpace(a);
            bool bw = !String.IsNullOrWhiteSpace(b);
            if (aw == bw) return 0;
            return aw ? -1 : 1;
        }

        private static int AirScoutSortValue(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return Int32.MaxValue;
            value = value.Trim();
            if (String.Equals(value, "NOW", StringComparison.OrdinalIgnoreCase)) return 0;
            if (value == "-") return Int32.MaxValue - 1;
            Match m = Regex.Match(value, @"-?\d+");
            int mins;
            return m.Success && Int32.TryParse(m.Value, out mins) ? Math.Max(0, mins) : Int32.MaxValue - 2;
        }

        private static int CompareNumericWithBlanksLast(string a, string b)
        {
            bool blankA = String.IsNullOrWhiteSpace(a);
            bool blankB = String.IsNullOrWhiteSpace(b);
            if (blankA && blankB) return 0;
            if (blankA) return 1;
            if (blankB) return -1;
            int ai = ExtractFirstInt(a);
            int bi = ExtractFirstInt(b);
            return ai.CompareTo(bi);
        }

        private static int ExtractFirstInt(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return 0;
            Match m = Regex.Match(value, @"-?\d+");
            if (!m.Success) return 0;
            int n;
            return Int32.TryParse(m.Value, out n) ? n : 0;
        }
    }

    internal sealed class DxRadioSnapshot
    {
        public string FrequencyText = "";
        public string FrequencyMhzText = "";
        public string Band = "";
        public string Mode = "";
    }

    internal enum TelnetKstStatus
    {
        Disconnected,
        WaitForLogin,
        WaitForPassword,
        WaitForRoomSelection,
        LoggedIn
    }

    internal sealed class TelnetKstClient : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private readonly string _username;
        private readonly string _password;
        private readonly int _room;
        private TcpClient _tcp;
        private NetworkStream _stream;
        private StreamReader _reader;
        private StreamWriter _writer;
        private CancellationTokenSource _cts;
        private Task _readTask;
        private StreamWriter _rawLog;
        private TelnetKstStatus _status = TelnetKstStatus.Disconnected;
        private readonly StringBuilder _line = new StringBuilder();

        public event EventHandler<string> LineReceived;
        public event EventHandler<string> StatusChanged;
        public event EventHandler LoggedIn;

        public bool IsConnected { get { return _tcp != null && _tcp.Connected; } }

        public TelnetKstClient(string host, int port, string username, string password, int room)
        {
            _host = host;
            _port = port;
            _username = username;
            _password = password;
            _room = room;
        }

        public async Task ConnectAsync()
        {
            _cts = new CancellationTokenSource();
            _tcp = new TcpClient();
            RaiseStatus("Connecting to " + _host + ":" + _port + "...");
            await _tcp.ConnectAsync(_host, _port);
            _stream = _tcp.GetStream();
            _reader = new StreamReader(_stream, Encoding.ASCII);
            _writer = new StreamWriter(_stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\r\n" };
            OpenRawLog();
            _status = TelnetKstStatus.WaitForLogin;
            RaiseStatus("Connected - waiting for Login prompt");
            _readTask = Task.Run(delegate { return ReadLoopAsync(_cts.Token); });
        }

        public Task SendCommandAsync(string command)
        {
            if (String.IsNullOrWhiteSpace(command)) return Task.FromResult(0);
            return SendRawLineAsync(command.Trim());
        }

        private async Task SendRawLineAsync(string line)
        {
            if (_writer == null) return;
            LogRaw("TX>", line);
            await _writer.WriteLineAsync(line);
        }

        private async Task ReadLoopAsync(CancellationToken token)
        {
            char[] buffer = new char[2048];
            try
            {
                while (!token.IsCancellationRequested && _reader != null)
                {
                    int read = await _reader.ReadAsync(buffer, 0, buffer.Length);
                    if (read <= 0) break;
                    string chunk = new string(buffer, 0, read);
                    LogRaw("RX<", chunk);
                    ProcessChunk(chunk);
                }
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { RaiseStatus("KST read error: " + ex.Message); }
            finally { RaiseStatus("KST connection closed."); }
        }

        private void ProcessChunk(string chunk)
        {
            if (String.IsNullOrEmpty(chunk)) return;
            for (int i = 0; i < chunk.Length; i++)
            {
                char ch = chunk[i];
                if (ch == '\r' || ch == '\n')
                {
                    string line = _line.ToString().Trim();
                    _line.Length = 0;
                    if (line.Length > 0) HandleLine(line);
                }
                else
                {
                    _line.Append(ch);
                    string current = _line.ToString();
                    if (_status == TelnetKstStatus.WaitForLogin && current.IndexOf("Login:", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        HandleLine(current);
                        _line.Length = 0;
                    }
                    else if (_status == TelnetKstStatus.WaitForPassword && current.IndexOf("Password:", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        HandleLine(current);
                        _line.Length = 0;
                    }
                }
            }
        }

        private void HandleLine(string data)
        {
            if (String.IsNullOrWhiteSpace(data)) return;
            RaiseLine(data);

            if (_status != TelnetKstStatus.LoggedIn && IsLoginFailureLine(data))
            {
                RaiseStatus("KST login failed: " + data.Trim());
                Dispose();
                return;
            }

            switch (_status)
            {
                case TelnetKstStatus.WaitForLogin:
                    if (data.IndexOf("Login:", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _ = SendRawLineAsync(_username.ToUpperInvariant());
                        _status = TelnetKstStatus.WaitForPassword;
                        RaiseStatus("User name sent");
                    }
                    break;

                case TelnetKstStatus.WaitForPassword:
                    if (data.IndexOf("Password:", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _ = SendRawLineAsync(_password);
                        _status = TelnetKstStatus.WaitForRoomSelection;
                        RaiseStatus("Password sent");
                    }
                    break;

                case TelnetKstStatus.WaitForRoomSelection:
                    if (data.IndexOf("Your choice", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _ = SendRawLineAsync(_room.ToString());
                        _status = TelnetKstStatus.LoggedIn;
                        RaiseStatus("Logged in - room " + _room + " selected");
                        var handler = LoggedIn;
                        if (handler != null) handler(this, EventArgs.Empty);
                    }
                    break;

                case TelnetKstStatus.LoggedIn:
                    if (data.IndexOf("__QUIT__", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        RaiseStatus("Disconnected by KST server");
                    }
                    break;
            }
        }

        private static bool IsLoginFailureLine(string data)
        {
            if (String.IsNullOrWhiteSpace(data)) return false;
            string text = data.ToUpperInvariant();
            return text.Contains("WRONG PASSWORD") ||
                   text.Contains("BAD PASSWORD") ||
                   text.Contains("INVALID PASSWORD") ||
                   text.Contains("LOGIN FAILED") ||
                   text.Contains("LOGIN INCORRECT") ||
                   text.Contains("ACCESS DENIED") ||
                   text.Contains("AUTHENTICATION FAILED") ||
                   text.Contains("INVALID CALL") ||
                   text.Contains("INVALID USER") ||
                   text.Contains("UNKNOWN USER") ||
                   text.Contains("NO SUCH USER") ||
                   text.Contains("CALLSIGN NOT FOUND") ||
                   text.Contains("NOT REGISTERED") ||
                   text.Contains("BAD LOGIN") ||
                   text.Contains("WRONG LOGIN") ||
                   text.Contains("INVALID LOGIN");
        }

        private void OpenRawLog()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DXLog.net", "KSTChatDXLog");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "kst-telnet-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".log");
                _rawLog = new StreamWriter(path, false, Encoding.UTF8);
                _rawLog.AutoFlush = true;
                _rawLog.WriteLine("# KST 23000 raw capture started UTC " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
                _rawLog.WriteLine("# Host=" + _host + " Port=" + _port + " Room=" + _room);
                RaiseStatus("Raw KST log: " + path);
            }
            catch (Exception ex) { RaiseStatus("Could not open raw KST log: " + ex.Message); }
        }

        private void LogRaw(string prefix, string value)
        {
            try
            {
                if (_rawLog != null)
                    _rawLog.WriteLine(DateTime.UtcNow.ToString("HH:mm:ss.fff") + " " + prefix + " " + value.Replace("\r", "<CR>").Replace("\n", "<LF>"));
            }
            catch { }
        }

        private void RaiseLine(string line)
        {
            var handler = LineReceived;
            if (handler != null) handler(this, line);
        }

        private void RaiseStatus(string status)
        {
            var handler = StatusChanged;
            if (handler != null) handler(this, status);
        }

        public void Dispose()
        {
            try { if (_cts != null) _cts.Cancel(); } catch { }
            try { if (_writer != null) _writer.Dispose(); } catch { }
            try { if (_reader != null) _reader.Dispose(); } catch { }
            try { if (_stream != null) _stream.Dispose(); } catch { }
            try { if (_tcp != null) _tcp.Close(); } catch { }
            try { if (_rawLog != null) _rawLog.Dispose(); } catch { }
        }
    }

    internal enum KstParsedType
    {
        Other,
        User,
        Chat,
        DxClusterSpot,
        Prompt
    }

    internal sealed class KstParsedLine
    {
        public KstParsedType Type;
        public string TimeText;
        public string Call;
        public string Name;
        public string Locator;
        public string Message;
        public bool Worked;
    }

    internal static class KstTextParser
    {
        public static KstParsedLine Parse(string myCall, string s)
        {
            KstParsedLine result = new KstParsedLine { Type = KstParsedType.Other, TimeText = DateTime.UtcNow.ToString("HH:mm") };
            if (String.IsNullOrWhiteSpace(s)) return result;
            s = s.Trim();

            Match user = Regex.Match(s, @"^\(?([A-Z0-9/]+)\)?\s+([A-R]{2}[0-9]{2}[A-X]{0,2})\s+(.+)$", RegexOptions.IgnoreCase);
            if (user.Success && !LooksLikeMenuLine(s))
            {
                result.Type = KstParsedType.User;
                result.Call = CleanCall(user.Groups[1].Value);
                result.Locator = user.Groups[2].Value.ToUpperInvariant();
                result.Name = DecodeDisplayText(user.Groups[3].Value);
                return result;
            }

            Match prompt = Regex.Match(s, @"^\d{4}Z\s+([A-Z0-9/]+)\s+.*chat>", RegexOptions.IgnoreCase);
            if (prompt.Success)
            {
                result.Type = KstParsedType.Prompt;
                result.Call = CleanCall(prompt.Groups[1].Value);
                return result;
            }

            // Common KST chat line: 1733Z CALL Name> message
            Match chat = Regex.Match(s, @"^(\d{4})Z\s+([A-Z0-9/]+)\s+([^>]{0,40})>\s*(.*)$", RegexOptions.IgnoreCase);
            if (chat.Success)
            {
                string call = CleanCall(chat.Groups[2].Value);
                string text = chat.Groups[4].Value.Trim();
                if (!String.IsNullOrWhiteSpace(call) && !String.IsNullOrWhiteSpace(text))
                {
                    result.Type = KstParsedType.Chat;
                    result.TimeText = chat.Groups[1].Value.Substring(0, 2) + ":" + chat.Groups[1].Value.Substring(2, 2);
                    result.Call = call;
                    result.Name = DecodeDisplayText(chat.Groups[3].Value);
                    result.Message = text;
                    return result;
                }
            }

            // DX cluster-ish line, based on the old dxKst parser treating "DX" as a spot marker.
            Match dx = Regex.Match(s, @"^(\d{4})Z\s+DX\s+de\s+([A-Z0-9/]+)\s+(.+)$", RegexOptions.IgnoreCase);
            if (dx.Success)
            {
                result.Type = KstParsedType.DxClusterSpot;
                result.TimeText = dx.Groups[1].Value.Substring(0, 2) + ":" + dx.Groups[1].Value.Substring(2, 2);
                result.Call = CleanCall(dx.Groups[2].Value);
                result.Message = "DX: " + dx.Groups[3].Value.Trim();
                return result;
            }

            return result;
        }

        private static bool LooksLikeMenuLine(string s)
        {
            return s.IndexOf("MHz", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   s.IndexOf("Your choice", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   s.IndexOf("Welcome", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string DecodeDisplayText(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "";

            // ON4KST sometimes sends names containing HTML entities rather than
            // literal characters, for example &#9889;, &#8482; or &amp;. Decode
            // twice at most so an accidentally double-encoded value is also
            // displayed correctly, without repeatedly transforming normal text.
            string decoded = value.Trim();
            for (int i = 0; i < 2; i++)
            {
                string next = WebUtility.HtmlDecode(decoded);
                if (String.Equals(next, decoded, StringComparison.Ordinal)) break;
                decoded = next;
            }

            return decoded.Replace('\u00A0', ' ').Trim();
        }

        private static string CleanCall(string call)
        {
            if (String.IsNullOrWhiteSpace(call)) return "";
            call = call.Trim().ToUpperInvariant();
            if (call.StartsWith("(") && call.EndsWith(")") && call.Length > 2) call = call.Substring(1, call.Length - 2);
            return Regex.Replace(call, "[^A-Z0-9/]+", "");
        }
    }

    internal sealed class KstUserInfo
    {
        public string Call;
        public string Name;
        public string Locator;
        public bool Dirty;
        public bool WorkedCurrentBand;
        public string WorkedBandKey = "";
        public bool WorkedCheckComplete;
        public int MissedRefreshes;
        public DateTime LastActivityUtc = DateTime.MinValue;
        public bool IsWatched;
    }

    internal sealed class KstAirScoutFilterOption
    {
        public int MaxMinutes { get; private set; }
        public KstAirScoutFilterOption(int maxMinutes) { MaxMinutes = maxMinutes; }
        public override string ToString()
        {
            if (MaxMinutes < 0) return "All";
            if (MaxMinutes == 0) return "NOW";
            return "≤" + MaxMinutes.ToString() + "m";
        }
    }

    internal enum KstAircraftFilterMode
    {
        All = 0,
        XXAndXXX = 1,
        XXXOnly = 2,
        PassengerOrCargo = 3,
        CargoOnly = 4
    }

    internal enum KstAircraftTrafficClass
    {
        Unknown = 0,
        Passenger = 1,
        Cargo = 2
    }

    internal sealed class KstAircraftFilterOption
    {
        public KstAircraftFilterMode Mode { get; private set; }

        public KstAircraftFilterOption(KstAircraftFilterMode mode)
        {
            Mode = mode;
        }

        public static List<KstAircraftFilterOption> GetOptions()
        {
            return new List<KstAircraftFilterOption>
            {
                new KstAircraftFilterOption(KstAircraftFilterMode.All),
                new KstAircraftFilterOption(KstAircraftFilterMode.XXAndXXX),
                new KstAircraftFilterOption(KstAircraftFilterMode.XXXOnly),
                new KstAircraftFilterOption(KstAircraftFilterMode.PassengerOrCargo),
                new KstAircraftFilterOption(KstAircraftFilterMode.CargoOnly)
            };
        }

        public static KstAircraftFilterMode NormalizeMode(int value)
        {
            return value >= (int)KstAircraftFilterMode.All && value <= (int)KstAircraftFilterMode.CargoOnly
                ? (KstAircraftFilterMode)value
                : KstAircraftFilterMode.All;
        }

        public override string ToString()
        {
            switch (Mode)
            {
                case KstAircraftFilterMode.XXAndXXX: return "XX and XXX";
                case KstAircraftFilterMode.XXXOnly: return "XXX only";
                case KstAircraftFilterMode.PassengerOrCargo: return "Passenger / cargo";
                case KstAircraftFilterMode.CargoOnly: return "Cargo only";
                default: return "All";
            }
        }
    }

    internal sealed class KstDistanceOption
    {
        public int MaxKm { get; private set; }

        public KstDistanceOption(int maxKm)
        {
            MaxKm = Math.Max(0, maxKm);
        }

        public override string ToString()
        {
            return MaxKm <= 0 ? "All" : "0-" + MaxKm.ToString() + " km";
        }
    }

    internal sealed class KstRoomOption
    {
        public int Room { get; private set; }
        public string Title { get; private set; }

        public KstRoomOption(int room, string title)
        {
            Room = room;
            Title = title ?? ("Room " + room.ToString());
        }

        public override string ToString()
        {
            return Title;
        }
    }

    internal static class KstRoomTitles
    {
        public static List<KstRoomOption> GetOptions()
        {
            List<KstRoomOption> result = new List<KstRoomOption>();
            for (int room = 1; room <= 13; room++) result.Add(new KstRoomOption(room, GetTitle(room)));
            return result;
        }

        public static string GetTitle(int room)
        {
            switch (room)
            {
                case 1: return "50/70 MHz";
                case 2: return "144/432 MHz";
                case 3: return "Microwave";
                case 4: return "EME";
                case 5: return "LowBand";
                case 6: return "50/70 MHz R3";
                case 7: return "50/70 MHz R2";
                case 8: return "144/432 MHz R2";
                case 9: return "144/432 MHz R3";
                case 10: return "kHz (2000-630 m)";
                case 11: return "WARC (30/17/12 m)";
                case 12: return "28 MHz";
                case 13: return "40 MHz";
                default: return "Room " + room.ToString();
            }
        }
    }

    internal sealed class KstWorkedBandOption
    {
        public string Key { get; private set; }
        public string Header { get; private set; }
        public string SetupLabel { get; private set; }

        public KstWorkedBandOption(string key, string header, string setupLabel)
        {
            Key = key;
            Header = header;
            SetupLabel = setupLabel;
        }
    }

    internal static class KstWorkedBands
    {
        public static readonly KstWorkedBandOption[] Options = new KstWorkedBandOption[]
        {
            new KstWorkedBandOption("50", "50", "50 MHz"),
            new KstWorkedBandOption("70", "70", "70 MHz"),
            new KstWorkedBandOption("144", "144", "144 MHz"),
            new KstWorkedBandOption("432", "432", "432 MHz"),
            new KstWorkedBandOption("1296", "1.3G", "1296 MHz"),
            new KstWorkedBandOption("2320", "2.3G", "2320 MHz"),
            new KstWorkedBandOption("3400", "3.4G", "3400 MHz"),
            new KstWorkedBandOption("5760", "5.7G", "5760 MHz"),
            new KstWorkedBandOption("10368", "10G", "10 GHz"),
            new KstWorkedBandOption("24048", "24G", "24 GHz"),
            new KstWorkedBandOption("47088", "47G", "47 GHz"),
            new KstWorkedBandOption("76032", "76G", "76 GHz")
        };

        public static List<KstWorkedBandOption> GetSelectedOptions(string[] selectedKeys)
        {
            HashSet<string> selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (selectedKeys != null)
            {
                foreach (string key in selectedKeys)
                {
                    string normalized = NormalizeKey(key);
                    if (!String.IsNullOrWhiteSpace(normalized)) selected.Add(normalized);
                }
            }
            List<KstWorkedBandOption> result = new List<KstWorkedBandOption>();
            foreach (KstWorkedBandOption option in Options)
                if (selected.Contains(option.Key)) result.Add(option);
            return result;
        }

        public static string NormalizeKey(string band)
        {
            if (String.IsNullOrWhiteSpace(band)) return "";
            string text = band.Trim().ToUpperInvariant().Replace(" ", "").Replace("MHZ", "").Replace("GHZ", "G");
            if (text.StartsWith("B") && text.Length > 1 && Char.IsDigit(text[1])) text = text.Substring(1);
            switch (text)
            {
                case "6M": return "50";
                case "4M": return "70";
                case "2M": return "144";
                case "70CM": return "432";
                case "23CM": case "1.3G": case "1.296G": return "1296";
                case "13CM": case "2.3G": case "2.32G": return "2320";
                case "9CM": case "3.4G": return "3400";
                case "6CM": case "5.7G": case "5.76G": return "5760";
                case "3CM": case "10G": case "10.368G": return "10368";
                case "1.25CM": case "24G": case "24.048G": return "24048";
                case "6MM": case "47G": case "47.088G": return "47088";
                case "4MM": case "76G": case "76.032G": return "76032";
            }
            double number;
            if (text.EndsWith("G") && Double.TryParse(text.Substring(0, text.Length - 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number))
                return NearestKnownKey(number * 1000.0);
            if (Double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number))
                return NearestKnownKey(number);
            return text;
        }

        private static string NearestKnownKey(double mhz)
        {
            string best = "";
            double bestDiff = Double.MaxValue;
            foreach (KstWorkedBandOption option in Options)
            {
                double candidate;
                if (!Double.TryParse(option.Key, out candidate)) continue;
                double diff = Math.Abs(candidate - mhz);
                if (diff < bestDiff) { bestDiff = diff; best = option.Key; }
            }
            return bestDiff <= Math.Max(2.0, mhz * 0.03) ? best : mhz.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    internal sealed class KstSettings
    {
        public string Host = "www.on4kst.info";
        public int Port = 23000;
        public int Room = 2;
        public int DistanceFilterKm = 0;
        public string Callsign = "";
        public string Password = "";
        public string Name = "";
        public string OwnLocator = "";
        public bool AirScoutEnabled = false;
        public int AirScoutPort = 9872;
        public int AirScoutHttpPort = 9880;
        public int AirScoutFilterMinutes = -1;
        public int AircraftFilterMode = 0;
        public bool AirScoutAutoSort = true;
        public bool UnworkedCurrentBandOnly = false;
        public bool AirScoutAlertsEnabled = false;
        public int AirScoutAlertMinutes = 5;
        public bool ShowAircraftTrails = true;
        public bool TurnRotatorOnStationClick = true;
        public int UserNameColumnWidth = 0;
        public string[] WatchedCalls = new string[0];
        public string[] WorkedBandColumns = new string[] { "144", "432" };
        public string[] Macros = new string[]
        {
            "PSE SKED {FREQ} {MODE}",
            "QRV {FREQ} {MODE}?",
            "I CALL YOU {FREQ} {MODE}",
            "TU 73"
        };
        public string[] Macros50_70;
        public string[] Macros144_432;
        public string[] Macros1296;
        public string[] MacrosMicrowave;
        public string[] MacrosEme;
        public Dictionary<string, string> StationNotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public int WindowX = Int32.MinValue;
        public int WindowY = Int32.MinValue;
        public int WindowW = 0;
        public int WindowH = 0;
        public string TitleBarColor = "";
        public int[] ColorValues = new int[0];

        public bool HasWindowBounds
        {
            get { return WindowX != Int32.MinValue && WindowY != Int32.MinValue && WindowW > 100 && WindowH > 100; }
        }

        private static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DXLog.net", "KstChatBridgeTelnet.ini"); }
        }

        public static KstSettings Load()
        {
            KstSettings s = new KstSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1);
                    int n;
                    if (key == "host") s.Host = val;
                    else if (key == "port" && Int32.TryParse(val, out n)) s.Port = n;
                    else if ((key == "room" || key == "chat") && Int32.TryParse(val, out n)) s.Room = n;
                    else if ((key == "distancefilterkm" || key == "distancekm") && Int32.TryParse(val, out n)) s.DistanceFilterKm = n;
                    else if (key == "callsign") s.Callsign = val;
                    else if (key == "password") s.Password = val;
                    else if (key == "name") s.Name = val;
                    else if (key == "locator" || key == "ownlocator" || key == "qthlocator") s.OwnLocator = val;
                    else if (key == "airscoutenabled") { bool b; if (Boolean.TryParse(val, out b)) s.AirScoutEnabled = b; }
                    else if (key == "airscoutport" && Int32.TryParse(val, out n) && n > 0 && n <= 65535) s.AirScoutPort = n;
                    else if (key == "airscouthttpport" && Int32.TryParse(val, out n) && n > 0 && n <= 65535) s.AirScoutHttpPort = n;
                    else if (key == "airscoutfilterminutes" && Int32.TryParse(val, out n)) s.AirScoutFilterMinutes = n;
                    else if (key == "aircraftfiltermode" && Int32.TryParse(val, out n)) s.AircraftFilterMode = Math.Max(0, Math.Min(4, n));
                    else if (key == "airscoutautosort") { bool b; if (Boolean.TryParse(val, out b)) s.AirScoutAutoSort = b; }
                    else if (key == "unworkedcurrentbandonly") { bool b; if (Boolean.TryParse(val, out b)) s.UnworkedCurrentBandOnly = b; }
                    else if (key == "airscoutalertsenabled") { bool b; if (Boolean.TryParse(val, out b)) s.AirScoutAlertsEnabled = b; }
                    else if (key == "airscoutalertminutes" && Int32.TryParse(val, out n)) s.AirScoutAlertMinutes = Math.Max(0, Math.Min(30, n));
                    else if (key == "showaircrafttrails") { bool b; if (Boolean.TryParse(val, out b)) s.ShowAircraftTrails = b; }
                    else if (key == "turnrotatoronstationclick") { bool b; if (Boolean.TryParse(val, out b)) s.TurnRotatorOnStationClick = b; }
                    else if (key == "usernamecolumnwidth" && Int32.TryParse(val, out n)) s.UserNameColumnWidth = Math.Max(0, Math.Min(420, n));
                    else if (key == "watchedcalls") s.WatchedCalls = (val ?? "").Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    else if (key == "workedbandcolumns" || key == "workedbands")
                    {
                        List<string> keys = new List<string>();
                        foreach (string part in (val ?? "").Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            string normalized = KstWorkedBands.NormalizeKey(part);
                            if (!String.IsNullOrWhiteSpace(normalized) && !keys.Contains(normalized)) keys.Add(normalized);
                        }
                        s.WorkedBandColumns = keys.ToArray();
                    }
                    else if (key == "windowx" && Int32.TryParse(val, out n)) s.WindowX = n;
                    else if (key == "windowy" && Int32.TryParse(val, out n)) s.WindowY = n;
                    else if (key == "windoww" && Int32.TryParse(val, out n)) s.WindowW = n;
                    else if (key == "windowh" && Int32.TryParse(val, out n)) s.WindowH = n;
                    else if (key == "titlebarcolor" || key == "titlebar") s.TitleBarColor = val;
                    else if (key.StartsWith("color"))
                    {
                        int idx;
                        if (Int32.TryParse(key.Substring(5), out idx) && idx >= 0 && idx < 20 && Int32.TryParse(val, out n))
                        {
                            if (s.ColorValues == null || s.ColorValues.Length < 20) Array.Resize(ref s.ColorValues, 20);
                            s.ColorValues[idx] = n;
                        }
                    }
                    else if (key.StartsWith("stationnote_"))
                    {
                        string call = key.Substring("stationnote_".Length).ToUpperInvariant();
                        try { s.StationNotes[call] = Encoding.UTF8.GetString(Convert.FromBase64String(val)); } catch { }
                    }
                    else if (TryLoadMacroProfile(s, key, val))
                    {
                    }
                    else if (key.StartsWith("macro"))
                    {
                        int idx;
                        if (Int32.TryParse(key.Substring(5), out idx) && idx >= 1 && idx <= 4)
                            s.Macros[idx - 1] = val;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                List<string> lines = new List<string>();
                lines.Add("host=" + Host);
                lines.Add("port=" + Port);
                lines.Add("room=" + Room);
                lines.Add("distancefilterkm=" + DistanceFilterKm);
                lines.Add("callsign=" + Callsign);
                lines.Add("password=" + Password);
                lines.Add("name=" + Name);
                lines.Add("locator=" + OwnLocator);
                lines.Add("airscoutenabled=" + AirScoutEnabled.ToString());
                lines.Add("airscoutport=" + AirScoutPort.ToString());
                lines.Add("airscouthttpport=" + AirScoutHttpPort.ToString());
                lines.Add("airscoutfilterminutes=" + AirScoutFilterMinutes.ToString());
                lines.Add("aircraftfiltermode=" + AircraftFilterMode.ToString());
                lines.Add("airscoutautosort=" + AirScoutAutoSort.ToString());
                lines.Add("unworkedcurrentbandonly=" + UnworkedCurrentBandOnly.ToString());
                lines.Add("airscoutalertsenabled=" + AirScoutAlertsEnabled.ToString());
                lines.Add("airscoutalertminutes=" + AirScoutAlertMinutes.ToString());
                lines.Add("showaircrafttrails=" + ShowAircraftTrails.ToString());
                lines.Add("turnrotatoronstationclick=" + TurnRotatorOnStationClick.ToString());
                lines.Add("usernamecolumnwidth=" + UserNameColumnWidth.ToString());
                lines.Add("watchedcalls=" + String.Join(",", WatchedCalls ?? new string[0]));
                lines.Add("workedbandcolumns=" + String.Join(",", WorkedBandColumns ?? new string[0]));
                lines.Add("macro1=" + (Macros != null && Macros.Length > 0 ? Macros[0] : ""));
                lines.Add("macro2=" + (Macros != null && Macros.Length > 1 ? Macros[1] : ""));
                lines.Add("macro3=" + (Macros != null && Macros.Length > 2 ? Macros[2] : ""));
                lines.Add("macro4=" + (Macros != null && Macros.Length > 3 ? Macros[3] : ""));
                SaveMacroProfile(lines, "50_70", Macros50_70);
                SaveMacroProfile(lines, "144_432", Macros144_432);
                SaveMacroProfile(lines, "1296", Macros1296);
                SaveMacroProfile(lines, "microwave", MacrosMicrowave);
                SaveMacroProfile(lines, "eme", MacrosEme);
                if (StationNotes != null)
                {
                    foreach (KeyValuePair<string, string> kv in StationNotes)
                    {
                        if (String.IsNullOrWhiteSpace(kv.Key) || String.IsNullOrWhiteSpace(kv.Value)) continue;
                        lines.Add("stationnote_" + kv.Key.ToUpperInvariant() + "=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(kv.Value)));
                    }
                }
                lines.Add("windowx=" + WindowX);
                lines.Add("windowy=" + WindowY);
                lines.Add("windoww=" + WindowW);
                lines.Add("windowh=" + WindowH);
                lines.Add("titlebarcolor=" + (TitleBarColor ?? ""));
                if (ColorValues != null)
                {
                    for (int i = 0; i < ColorValues.Length && i < 20; i++)
                        lines.Add("color" + i.ToString() + "=" + ColorValues[i].ToString());
                }
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch { }
        }

        private static string[] CloneMacroArray(string[] values)
        {
            if (values == null) return null;
            string[] copy = new string[4];
            for (int i = 0; i < 4; i++) copy[i] = i < values.Length ? (values[i] ?? "") : "";
            return copy;
        }

        private static bool TryLoadMacroProfile(KstSettings settings, string key, string value)
        {
            string[] prefixes = new string[] { "macro50_70_", "macro144_432_", "macro1296_", "macromicrowave_", "macroeme_" };
            for (int p = 0; p < prefixes.Length; p++)
            {
                if (!key.StartsWith(prefixes[p])) continue;
                int idx;
                if (!Int32.TryParse(key.Substring(prefixes[p].Length), out idx) || idx < 1 || idx > 4) return true;
                string[] target = settings.GetMacroSetStorage(prefixes[p]);
                target[idx - 1] = value;
                return true;
            }
            return false;
        }

        private string[] GetMacroSetStorage(string prefix)
        {
            if (prefix == "macro50_70_") return Macros50_70 ?? (Macros50_70 = CloneMacroArray(Macros));
            if (prefix == "macro144_432_") return Macros144_432 ?? (Macros144_432 = CloneMacroArray(Macros));
            if (prefix == "macro1296_") return Macros1296 ?? (Macros1296 = CloneMacroArray(Macros));
            if (prefix == "macromicrowave_") return MacrosMicrowave ?? (MacrosMicrowave = CloneMacroArray(Macros));
            return MacrosEme ?? (MacrosEme = CloneMacroArray(Macros));
        }

        private static void SaveMacroProfile(List<string> lines, string profile, string[] values)
        {
            if (values == null) return;
            for (int i = 0; i < 4; i++) lines.Add("macro" + profile + "_" + (i + 1).ToString() + "=" + (i < values.Length ? (values[i] ?? "") : ""));
        }

        public string[] GetMacroSet(string profile)
        {
            string[] values = profile == "50_70" ? Macros50_70 : profile == "144_432" ? Macros144_432 : profile == "1296" ? Macros1296 : profile == "microwave" ? MacrosMicrowave : profile == "eme" ? MacrosEme : Macros;
            return CloneMacroArray(values ?? Macros);
        }

        public void SetMacroSet(string profile, string[] values)
        {
            string[] copy = CloneMacroArray(values);
            if (profile == "50_70") Macros50_70 = copy;
            else if (profile == "144_432") Macros144_432 = copy;
            else if (profile == "1296") Macros1296 = copy;
            else if (profile == "microwave") MacrosMicrowave = copy;
            else if (profile == "eme") MacrosEme = copy;
            else Macros = copy;
        }

        public KstSettings Clone()
        {
            string[] m = new string[] { "", "", "", "" };
            if (Macros != null)
            {
                for (int i = 0; i < Math.Min(4, Macros.Length); i++) m[i] = Macros[i];
            }
            int[] colors = new int[0];
            if (ColorValues != null)
            {
                colors = new int[ColorValues.Length];
                Array.Copy(ColorValues, colors, ColorValues.Length);
            }
            string[] workedColumns = WorkedBandColumns == null ? new string[0] : (string[])WorkedBandColumns.Clone();
            string[] watched = WatchedCalls == null ? new string[0] : (string[])WatchedCalls.Clone();
            Dictionary<string, string> notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (StationNotes != null) foreach (KeyValuePair<string, string> kv in StationNotes) notes[kv.Key] = kv.Value;
            return new KstSettings { Host = Host, Port = Port, Room = Room, DistanceFilterKm = DistanceFilterKm, Callsign = Callsign, Password = Password, Name = Name, OwnLocator = OwnLocator, AirScoutEnabled = AirScoutEnabled, AirScoutPort = AirScoutPort, AirScoutHttpPort = AirScoutHttpPort, AirScoutFilterMinutes = AirScoutFilterMinutes, AircraftFilterMode = AircraftFilterMode, AirScoutAutoSort = AirScoutAutoSort, UnworkedCurrentBandOnly = UnworkedCurrentBandOnly, AirScoutAlertsEnabled = AirScoutAlertsEnabled, AirScoutAlertMinutes = AirScoutAlertMinutes, ShowAircraftTrails = ShowAircraftTrails, TurnRotatorOnStationClick = TurnRotatorOnStationClick, UserNameColumnWidth = UserNameColumnWidth, WatchedCalls = watched, WorkedBandColumns = workedColumns, Macros = m, Macros50_70 = CloneMacroArray(Macros50_70), Macros144_432 = CloneMacroArray(Macros144_432), Macros1296 = CloneMacroArray(Macros1296), MacrosMicrowave = CloneMacroArray(MacrosMicrowave), MacrosEme = CloneMacroArray(MacrosEme), StationNotes = notes, WindowX = WindowX, WindowY = WindowY, WindowW = WindowW, WindowH = WindowH, TitleBarColor = TitleBarColor, ColorValues = colors };
        }
    }

    internal sealed class KstRoomDialog : Form
    {
        private readonly ComboBox _room;
        public int Room { get; private set; }

        public KstRoomDialog(int currentRoom)
        {
            Room = currentRoom <= 0 ? 2 : currentRoom;
            Text = "KST room";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(430, 130);

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 2 };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

            TableLayoutPanel row = new TableLayoutPanel { Dock = DockStyle.Top, Height = 34, ColumnCount = 2, Margin = new Padding(0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.Controls.Add(new Label { Text = "Room", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            _room = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (KstRoomOption option in KstRoomTitles.GetOptions()) _room.Items.Add(option);
            for (int i = 0; i < _room.Items.Count; i++)
            {
                KstRoomOption option = _room.Items[i] as KstRoomOption;
                if (option != null && option.Room == Room) { _room.SelectedIndex = i; break; }
            }
            row.Controls.Add(_room, 1, 0);

            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 5, 0, 0) };
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(88, 26), Margin = new Padding(6, 0, 0, 0) };
            Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Size = new Size(88, 26) };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            root.Controls.Add(row, 0, 0);
            root.Controls.Add(buttons, 0, 1);
            Controls.Add(root);
            AcceptButton = ok;
            CancelButton = cancel;
            ok.Click += delegate
            {
                KstRoomOption selected = _room.SelectedItem as KstRoomOption;
                if (selected != null) Room = selected.Room;
            };
        }
    }

    internal sealed class KstSetupDialog : Form
    {
        private TextBox _host;
        private NumericUpDown _port;
        private ComboBox _room;
        private TextBox _call;
        private TextBox _pass;
        private TextBox _name;
        private TextBox _locator;
        private CheckBox _airScoutEnabled;
        private NumericUpDown _airScoutPort;
        private NumericUpDown _airScoutHttpPort;
        private Panel _airScoutPortsPanel;
        private CheckBox _airScoutAlertsEnabled;
        private NumericUpDown _airScoutAlertMinutes;
        private Label _airScoutAlertMinutesLabel;
        private CheckBox _showAircraftTrails;
        private readonly ToolTip _toolTip;
        private readonly Dictionary<string, CheckBox> _workedBandChecks = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        public KstSettings Settings { get; private set; }

        public KstSetupDialog(KstSettings settings)
        {
            Settings = settings.Clone();
            _toolTip = new ToolTip { AutoPopDelay = 12000, InitialDelay = 250, ReshowDelay = 100, ShowAlways = true };
            Text = "KST Chat Bridge configuration";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(650, 645);

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 5 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 177));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            GroupBox connection = new GroupBox { Text = "KST connection", Dock = DockStyle.Fill, Padding = new Padding(10, 18, 10, 8) };
            TableLayoutPanel connectionGrid = CreateDxLogGrid(3, 105);
            _host = new TextBox { Text = Settings.Host, Dock = DockStyle.Fill };
            _port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = Math.Max(1, Math.Min(65535, Settings.Port)), Width = 105, Anchor = AnchorStyles.Left };
            _room = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            foreach (KstRoomOption option in KstRoomTitles.GetOptions()) _room.Items.Add(option);
            for (int i = 0; i < _room.Items.Count; i++)
            {
                KstRoomOption option = _room.Items[i] as KstRoomOption;
                if (option != null && option.Room == Settings.Room) { _room.SelectedIndex = i; break; }
            }
            AddDxLogRow(connectionGrid, 0, "Host", _host);
            AddDxLogRow(connectionGrid, 1, "Port", _port);
            AddDxLogRow(connectionGrid, 2, "Room", _room);
            connection.Controls.Add(connectionGrid);

            GroupBox station = new GroupBox { Text = "Station details", Dock = DockStyle.Fill, Padding = new Padding(10, 18, 10, 8) };
            TableLayoutPanel stationGrid = CreateDxLogGrid(4, 105);
            _call = new TextBox { Text = Settings.Callsign, Dock = DockStyle.Fill };
            _pass = new TextBox { Text = Settings.Password, UseSystemPasswordChar = true, Dock = DockStyle.Fill };
            _name = new TextBox { Text = Settings.Name, Dock = DockStyle.Fill };
            _locator = new TextBox { Text = Settings.OwnLocator, Width = 120, Anchor = AnchorStyles.Left };
            AddDxLogRow(stationGrid, 0, "User / call", _call);
            AddDxLogRow(stationGrid, 1, "Password", _pass);
            AddDxLogRow(stationGrid, 2, "Name", _name);
            AddDxLogRow(stationGrid, 3, "QTH locator", _locator);
            station.Controls.Add(stationGrid);

            GroupBox workedBands = new GroupBox { Text = "Worked-band columns", Dock = DockStyle.Fill, Padding = new Padding(10, 18, 10, 8) };
            TableLayoutPanel workedGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3, Margin = new Padding(0), Padding = new Padding(0) };
            for (int col = 0; col < 4; col++) workedGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            for (int row = 0; row < 3; row++) workedGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
            HashSet<string> selectedWorked = new HashSet<string>(Settings.WorkedBandColumns ?? new string[0], StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < KstWorkedBands.Options.Length; i++)
            {
                KstWorkedBandOption option = KstWorkedBands.Options[i];
                CheckBox check = new CheckBox { Text = option.SetupLabel, Checked = selectedWorked.Contains(option.Key), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(5, 2, 5, 2) };
                _workedBandChecks[option.Key] = check;
                workedGrid.Controls.Add(check, i % 4, i / 4);
            }
            workedBands.Controls.Add(workedGrid);

            GroupBox airScout = new GroupBox { Text = "AirScout", Dock = DockStyle.Fill, Padding = new Padding(10, 18, 10, 8) };
            _airScoutEnabled = new CheckBox { Text = "Enable AirScout integration", Checked = Settings.AirScoutEnabled, AutoSize = true, Location = new Point(12, 25) };
            _airScoutPortsPanel = new Panel { Location = new Point(12, 50), Size = new Size(590, 34) };
            _airScoutPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = Settings.AirScoutPort > 0 ? Settings.AirScoutPort : 9872, Location = new Point(72, 4), Width = 82 };
            _airScoutHttpPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = Settings.AirScoutHttpPort > 0 ? Settings.AirScoutHttpPort : 9880, Location = new Point(254, 4), Width = 82 };
            _airScoutPortsPanel.Controls.Add(new Label { Text = "UDP port", Location = new Point(0, 7), AutoSize = true });
            _airScoutPortsPanel.Controls.Add(_airScoutPort);
            _airScoutPortsPanel.Controls.Add(new Label { Text = "HTTP port", Location = new Point(182, 7), AutoSize = true });
            _airScoutPortsPanel.Controls.Add(_airScoutHttpPort);
            _airScoutAlertsEnabled = new CheckBox { Text = "Alert for upcoming opportunities", Checked = Settings.AirScoutAlertsEnabled, AutoSize = true, Location = new Point(12, 88) };
            _airScoutAlertMinutes = new NumericUpDown { Minimum = 0, Maximum = 30, Value = Math.Max(0, Math.Min(30, Settings.AirScoutAlertMinutes)), Location = new Point(236, 85), Width = 55 };
            _airScoutAlertMinutesLabel = new Label { Text = "minutes", AutoSize = true, Location = new Point(296, 89) };
            _showAircraftTrails = new CheckBox { Text = "Show aircraft trails on map", Checked = Settings.ShowAircraftTrails, AutoSize = true, Location = new Point(390, 88) };
            airScout.Controls.Add(_airScoutEnabled);
            airScout.Controls.Add(_airScoutPortsPanel);
            airScout.Controls.Add(_airScoutAlertsEnabled);
            airScout.Controls.Add(_airScoutAlertMinutes);
            airScout.Controls.Add(_airScoutAlertMinutesLabel);
            airScout.Controls.Add(_showAircraftTrails);
            _airScoutEnabled.CheckedChanged += delegate { UpdateAirScoutVisibility(); };
            UpdateAirScoutVisibility();

            // Match DXLog's configuration dialogs: OK and Cancel form a centred
            // button pair rather than being pushed to the far right.
            TableLayoutPanel buttons = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Padding = new Padding(0, 8, 0, 0),
                Margin = new Padding(0)
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Size = new Size(88, 27), Anchor = AnchorStyles.Top, Margin = new Padding(3, 0, 3, 0) };
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(88, 27), Anchor = AnchorStyles.Top, Margin = new Padding(3, 0, 3, 0) };
            buttons.Controls.Add(ok, 1, 0);
            buttons.Controls.Add(cancel, 2, 0);

            root.Controls.Add(connection, 0, 0);
            root.Controls.Add(station, 0, 1);
            root.Controls.Add(workedBands, 0, 2);
            root.Controls.Add(airScout, 0, 3);
            root.Controls.Add(buttons, 0, 4);
            Controls.Add(root);
            AcceptButton = ok;
            CancelButton = cancel;

            _toolTip.SetToolTip(_host, "ON4KST classic telnet host name");
            _toolTip.SetToolTip(_port, "ON4KST classic telnet port; normally 23000");
            _toolTip.SetToolTip(_room, "Default ON4KST room shown in the main window");
            _toolTip.SetToolTip(_call, "Your ON4KST username or amateur-radio callsign");
            _toolTip.SetToolTip(_pass, "Your ON4KST password");
            _toolTip.SetToolTip(_name, "Name sent to ON4KST when logging in");
            _toolTip.SetToolTip(_locator, "Your own Maidenhead QTH locator used for QRB, QTF, map and AirScout calculations");
            foreach (KeyValuePair<string, CheckBox> pair in _workedBandChecks)
                _toolTip.SetToolTip(pair.Value, "Show a narrow worked-status column for " + pair.Value.Text + " in the KST user list");
            _toolTip.SetToolTip(_airScoutEnabled, "Enable UDP path queries and HTTP aircraft data from the external AirScout program");
            _toolTip.SetToolTip(_airScoutPort, "AirScout UDP server port used for path requests; normally 9872");
            _toolTip.SetToolTip(_airScoutHttpPort, "AirScout HTTP server port used for live aircraft positions; normally 9880");
            _toolTip.SetToolTip(_airScoutAlertsEnabled, "Notify when a visible or watched station enters the selected AirScout opportunity window");
            _toolTip.SetToolTip(_airScoutAlertMinutes, "Alert threshold in minutes before an AirScout opportunity");
            _toolTip.SetToolTip(_showAircraftTrails, "Show approximately 90 seconds of recent movement behind matched aircraft on the map");
            _toolTip.SetToolTip(ok, "Save these settings and close the dialog");
            _toolTip.SetToolTip(cancel, "Close without saving changes");
            FormClosed += delegate { if (_toolTip != null) _toolTip.Dispose(); };

            ok.Click += delegate
            {
                KstRoomOption selected = _room.SelectedItem as KstRoomOption;
                Settings.Host = _host.Text.Trim();
                Settings.Port = (int)_port.Value;
                if (selected != null) Settings.Room = selected.Room;
                Settings.Callsign = _call.Text.Trim().ToUpperInvariant();
                Settings.Password = _pass.Text;
                Settings.Name = _name.Text.Trim();
                Settings.OwnLocator = _locator.Text.Trim().ToUpperInvariant();
                Settings.AirScoutEnabled = _airScoutEnabled.Checked;
                Settings.AirScoutPort = (int)_airScoutPort.Value;
                Settings.AirScoutHttpPort = (int)_airScoutHttpPort.Value;
                Settings.AirScoutAlertsEnabled = _airScoutAlertsEnabled.Checked;
                Settings.AirScoutAlertMinutes = (int)_airScoutAlertMinutes.Value;
                Settings.ShowAircraftTrails = _showAircraftTrails.Checked;
                List<string> selectedBands = new List<string>();
                foreach (KstWorkedBandOption option in KstWorkedBands.Options)
                {
                    CheckBox check;
                    if (_workedBandChecks.TryGetValue(option.Key, out check) && check.Checked) selectedBands.Add(option.Key);
                }
                Settings.WorkedBandColumns = selectedBands.ToArray();
            };

            Shown += delegate { _call.Focus(); _call.SelectionStart = _call.Text.Length; };
        }

        private void UpdateAirScoutVisibility()
        {
            bool visible = _airScoutEnabled != null && _airScoutEnabled.Checked;
            if (_airScoutPortsPanel != null)
            {
                _airScoutPortsPanel.Visible = visible;
                _airScoutPortsPanel.Enabled = visible;
            }
            if (_airScoutAlertsEnabled != null) { _airScoutAlertsEnabled.Visible = visible; _airScoutAlertsEnabled.Enabled = visible; }
            if (_airScoutAlertMinutes != null) { _airScoutAlertMinutes.Visible = visible; _airScoutAlertMinutes.Enabled = visible; }
            if (_airScoutAlertMinutesLabel != null) { _airScoutAlertMinutesLabel.Visible = visible; _airScoutAlertMinutesLabel.Enabled = visible; }
            if (_showAircraftTrails != null) { _showAircraftTrails.Visible = visible; _showAircraftTrails.Enabled = visible; }
        }

        private static TableLayoutPanel CreateDxLogGrid(int rows, int labelWidth)
        {
            TableLayoutPanel grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = rows, Margin = new Padding(0), Padding = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < rows; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
            return grid;
        }

        private static void AddDxLogRow(TableLayoutPanel grid, int row, string labelText, Control control)
        {
            Label label = new Label { Text = labelText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(2, 0, 4, 0) };
            control.Margin = new Padding(2, 3, 2, 3);
            grid.Controls.Add(label, 0, row);
            grid.Controls.Add(control, 1, row);
        }
    }

    internal sealed class KstMacroDialog : Form
    {
        private readonly TextBox[] _boxes = new TextBox[4];
        public string[] Macros { get; private set; }

        public KstMacroDialog(string[] macros) : this(macros, 0, "")
        {
        }

        public KstMacroDialog(string[] macros, int focusIndex, string profileTitle = "")
        {
            Text = String.IsNullOrWhiteSpace(profileTitle) ? "KST macros" : "KST macros - " + profileTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(600, 185);
            ShowInTaskbar = false;

            Macros = new string[] { "", "", "", "" };
            if (macros != null)
            {
                for (int i = 0; i < Math.Min(4, macros.Length); i++) Macros[i] = macros[i] ?? "";
            }

            TableLayoutPanel p = new TableLayoutPanel();
            p.Dock = DockStyle.Fill;
            p.Padding = new Padding(12, 10, 12, 4);
            p.ColumnCount = 2;
            p.RowCount = 4;
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) p.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            for (int i = 0; i < 4; i++)
            {
                _boxes[i] = new TextBox { Text = Macros[i], Dock = DockStyle.Fill, Margin = new Padding(3, 4, 3, 3) };
                p.Controls.Add(new Label { Text = "M" + (i + 1).ToString(), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, i);
                p.Controls.Add(_boxes[i], 1, i);
            }


            Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 88, Height = 27, Margin = new Padding(4, 7, 4, 0) };
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 88, Height = 27, Margin = new Padding(4, 7, 4, 0) };

            FlowLayoutPanel buttonPair = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            buttonPair.Controls.Add(ok);
            buttonPair.Controls.Add(cancel);

            TableLayoutPanel buttons = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 41,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 192));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttons.Controls.Add(buttonPair, 1, 0);

            Controls.Add(p);
            Controls.Add(buttons);
            AcceptButton = ok;
            CancelButton = cancel;
            ToolTip macroTip = new ToolTip { AutoPopDelay = 12000, InitialDelay = 250, ReshowDelay = 100, ShowAlways = true };
            for (int i = 0; i < _boxes.Length; i++)
                macroTip.SetToolTip(_boxes[i], "Edit M" + (i + 1).ToString() + ". Supported tokens include {CALL}, {MYCALL}, {FREQ}, {BAND}, {MODE}, {LOC}, {MYLOC}, {QTF}, {QRB}, {AS}, {AIRCRAFT} and {ASMIN}");
            macroTip.SetToolTip(ok, "Save this macro profile");
            macroTip.SetToolTip(cancel, "Close without saving macro changes");
            FormClosed += delegate { macroTip.Dispose(); };
            ok.Click += delegate
            {
                for (int i = 0; i < 4; i++) Macros[i] = _boxes[i].Text;
            };
            Shown += delegate
            {
                int index = Math.Max(0, Math.Min(3, focusIndex));
                _boxes[index].Focus();
                _boxes[index].SelectionStart = _boxes[index].Text.Length;
            };
        }
    }

    internal sealed class BufferedListView : ListView
    {
        public BufferedListView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            UpdateStyles();
        }
    }

    internal static class MessagePrompt
    {
        public static string Show(IWin32Window owner, string title, string label, string initial)
        {
            using (Form f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.Width = 520;
                f.Height = 150;

                TableLayoutPanel p = new TableLayoutPanel();
                p.Dock = DockStyle.Fill;
                p.Padding = new Padding(10);
                p.ColumnCount = 2;
                p.RowCount = 3;
                p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
                p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                p.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
                p.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
                p.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));

                TextBox box = new TextBox { Text = initial ?? "", Dock = DockStyle.Fill };
                Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90 };
                Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
                FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
                buttons.Controls.Add(ok);
                buttons.Controls.Add(cancel);

                p.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, 0);
                p.Controls.Add(box, 1, 0);
                p.Controls.Add(buttons, 0, 2); p.SetColumnSpan(buttons, 2);
                f.Controls.Add(p);
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                f.Shown += delegate { box.Focus(); box.SelectionStart = box.Text.Length; };

                return f.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
            }
        }
    }
}
