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
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using DXLog.net;
using DXLogDAL;

namespace DXLog.net
{
    public class KstChatBridge : KForm
    {
        private const int UserListPanelWidth = 620;
        private const int UserListPanelMinWidth = 600;

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
        private TextBox _roomTitleBox;
        private Button _roomButton;
        private Button _mapButton;
        private KstUserMapForm _mapForm;
        private TextBox _userBox;
        private TextBox _passBox;
        private Button _setupButton;
        private Button _connectButton;
        private Button _disconnectButton;
        private SplitContainer _split;
        private SplitContainer _messageSplit;
        private ListView _users;
        private ListView _messages;
        private ListView _threadMessages;
        private Label _threadHeaderLabel;
        private Button _sendButton;
        private Button _cqButton;
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
        private readonly object _airScoutPlaneLock = new object();
        private readonly Dictionary<string, AirScoutLivePlane> _airScoutPlaneById = new Dictionary<string, AirScoutLivePlane>(StringComparer.OrdinalIgnoreCase);
        private bool _airScoutPlaneFetchRunning;
        private DateTime _lastAirScoutPlaneFetchUtc = DateTime.MinValue;
        private string _airScoutPlaneFeedStatus = "Aircraft not read";
        private Button[] _macroButtons;
        private Button _editMacrosButton;
        private System.Windows.Forms.Timer _userRefreshTimer;
        private System.Windows.Forms.Timer _qsoLoggedRefreshTimer;
        private bool _subscribedNewQsoSaved;
        private System.Windows.Forms.Timer _inputFocusTimer;
        private Control _inputFocusTarget;
        private DateTime _inputFocusUntilUtc;
        private bool _composeDialogOpen;
        private bool _loadedPersistentColors;
        private bool _restoringWindowBounds;
        private System.Windows.Forms.Timer _persistSaveTimer;
        private System.Windows.Forms.Timer _startupBoundsRestoreTimer;
        private bool _startupPositionRestoreDone;
        private bool _allowPositionSave;
        private DateTime _lastUserLayoutChangeUtc;

        private readonly Dictionary<string, KstUserInfo> _userMap = new Dictionary<string, KstUserInfo>(StringComparer.OrdinalIgnoreCase);
        private string _lastSelectedCall;
        private bool _refreshingUserList;
        private int _userSortColumn = 0;
        private SortOrder _userSortOrder = SortOrder.Ascending;

        public KstChatBridge()
        {
            ConfigureIdentityForDxLog();
            _settings = KstSettings.Load();
            ConfigureColorSet();
            BuildUi();
        }

        public KstChatBridge(ContestData cdata)
        {
            ConfigureIdentityForDxLog();
            _contestData = cdata;
            _settings = KstSettings.Load();
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
            InitializeLayout();
            // The DXLog colour/font menu updates KForm.FormLayout and then raises this event.
            // Save immediately so colour changes survive closing/reopening this custom form.
            SavePersistentUiSettings();
        }

        public override void InitializeLayout()
        {
            // DXLog applies its saved FormLayout after constructing a custom form.
            // Re-apply the plugin INI values here, before KForm.InitializeLayout(),
            // so position, size, colours and title-bar colour win when the form is reopened.
            ApplySavedSettingsToFormLayoutBeforeInitialize();

            base.InitializeLayout(_windowFont);
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
                if (_inputFocusTimer != null)
                {
                    _inputFocusTimer.Stop();
                    _inputFocusTimer.Dispose();
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
            SaveLayoutAfterUserMoveOrResize();
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
            MinimumSize = new Size(940, 360);
            if (Width < 1180) Width = 1180;
            if (Height < 460) Height = 460;

            _layout = new TableLayoutPanel();
            _layout.Dock = DockStyle.Fill;
            _layout.ColumnCount = 12;
            _layout.RowCount = 4;
            _layout.Padding = new Padding(6);
            _layout.ColumnStyles.Clear();
            for (int i = 0; i < 12; i++) _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 8.3333f));
            _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

            // Host/port/user/pass are deliberately configured in Setup only.  The boxes
            // still exist internally so the existing connect/settings code can remain simple.
            _hostBox = new TextBox { Text = _settings.Host };
            _portBox = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = _settings.Port };
            _userBox = new TextBox { Text = _settings.Callsign };
            _passBox = new TextBox { Text = _settings.Password, UseSystemPasswordChar = true };

            _setupButton = new Button { Text = "Setup", Dock = DockStyle.Fill };
            _connectButton = new Button { Text = "Connect", Dock = DockStyle.Fill };
            _disconnectButton = new Button { Text = "Disconnect", Dock = DockStyle.Fill, Enabled = false };
            _roomButton = new Button { Text = "Room", Dock = DockStyle.Fill };
            _mapButton = new Button { Text = "Map", Dock = DockStyle.Fill };
            _roomTitleBox = new TextBox { Text = KstRoomTitles.GetTitle(_settings.Room), Dock = DockStyle.Fill, ReadOnly = true, BackColor = SystemColors.Window, Cursor = Cursors.Hand };

            // Clean top row:
            //   Setup on the left, Room + room title in the centre,
            //   Connect and Disconnect together on the top-right.
            _layout.Controls.Add(_setupButton, 0, 0);
            _layout.Controls.Add(_roomButton, 4, 0);
            _layout.Controls.Add(_roomTitleBox, 5, 0); _layout.SetColumnSpan(_roomTitleBox, 2);
            _layout.Controls.Add(_mapButton, 7, 0);
            _layout.Controls.Add(_connectButton, 9, 0);
            _layout.Controls.Add(_disconnectButton, 10, 0); _layout.SetColumnSpan(_disconnectButton, 2);

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

            _users = new ListView();
            _users.Dock = DockStyle.Fill;
            _users.View = View.Details;
            _users.FullRowSelect = true;
            _users.GridLines = true;
            _users.HideSelection = false;
            _users.OwnerDraw = true;
            _users.DrawColumnHeader += DrawListColumnHeader;
            _users.DrawSubItem += DrawListSubItem;
            _users.ColumnClick += UsersColumnClick;
            _users.ColumnWidthChanging += UsersColumnWidthChanging;
            _users.Resize += delegate { AdjustUserColumns(); };
            _users.Columns.Add("Call", 90);
            _users.Columns.Add("Name", 165);
            _users.Columns.Add("Loc", 75);
            _users.Columns.Add("QTF", 60);
            _users.Columns.Add("QRB", 85);
            _users.Columns.Add("AS", 48);
            _users.ShowItemToolTips = true;
            _users.DoubleClick += delegate { PutSelectedUserIntoDxLog(); };
            _users.MouseUp += UsersMouseUp;
            _users.SelectedIndexChanged += delegate { UsersSelectedIndexChanged(); };

            _messageSplit = new SplitContainer();
            _messageSplit.Dock = DockStyle.Fill;
            _messageSplit.Orientation = Orientation.Horizontal;
            _messageSplit.Panel1MinSize = 120;
            _messageSplit.Panel2MinSize = 90;
            _messageSplit.SplitterWidth = 5;
            _messageSplit.SizeChanged += delegate { ApplyMessageSplitSize(); };

            _messages = CreateMessageListView();
            _messages.DoubleClick += delegate { PutSelectedMessageIntoDxLog(); };
            _messages.MouseUp += MessagesMouseUp;
            _messages.SelectedIndexChanged += delegate { UpdateLastSelectedCallFromMessageList(_messages); RestyleMessages(); RefreshConversationView(); };

            Panel threadPanel = new Panel();
            threadPanel.Dock = DockStyle.Fill;
            _threadHeaderLabel = new Label { Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Text = "Selected station messages", Padding = new Padding(4, 0, 0, 0) };
            _threadMessages = CreateMessageListView();
            _threadMessages.DoubleClick += delegate { PutSelectedThreadMessageIntoDxLog(); };
            _threadMessages.MouseUp += MessagesMouseUp;
            _threadMessages.SelectedIndexChanged += delegate { UpdateLastSelectedCallFromMessageList(_threadMessages); RestyleThreadMessages(); };
            threadPanel.Controls.Add(_threadMessages);
            threadPanel.Controls.Add(_threadHeaderLabel);

            _messageSplit.Panel1.Controls.Add(_messages);
            _messageSplit.Panel2.Controls.Add(threadPanel);

            _split.Panel1.Controls.Add(_users);
            _split.Panel2.Controls.Add(_messageSplit);
            _layout.Controls.Add(_split, 0, 1); _layout.SetColumnSpan(_split, 12);

            _sendButton = new Button { Text = "CQ", Dock = DockStyle.Fill, Enabled = false };
            _cqButton = new Button { Text = "To call", Dock = DockStyle.Fill, Enabled = false };
            _layout.Controls.Add(_sendButton, 0, 2);
            _layout.Controls.Add(_cqButton, 1, 2);

            _macroButtons = new Button[4];
            for (int i = 0; i < _macroButtons.Length; i++)
            {
                int macroIndex = i;
                _macroButtons[i] = new Button { Text = "M" + (i + 1).ToString(), Dock = DockStyle.Fill, Enabled = false };
                _macroButtons[i].Click += async delegate { await SendMacroClicked(macroIndex); };
                _layout.Controls.Add(_macroButtons[i], i + 2, 2);
            }
            _editMacrosButton = new Button { Text = "Edit macros", Dock = DockStyle.Fill };
            _editMacrosButton.Click += delegate { EditMacrosClicked(); };
            // Keep the macro editor on the far-right of the button row.
            _layout.Controls.Add(_editMacrosButton, 10, 2); _layout.SetColumnSpan(_editMacrosButton, 2);

            _statusLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Text = "Ready" };
            _airScoutStatusLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Text = "AirScout: Off" };
            _layout.Controls.Add(_statusLabel, 0, 3); _layout.SetColumnSpan(_statusLabel, 9);
            _layout.Controls.Add(_airScoutStatusLabel, 9, 3); _layout.SetColumnSpan(_airScoutStatusLabel, 3);

            Controls.Add(_layout);

            // Save our own settings as well as DXLog's FormLayout.  DXLog does not
            // always persist custom-form layout/colours immediately when a custom
            // window is closed and reopened, so this plugin keeps its own INI too.
            FormClosing += delegate { SavePersistentUiSettings(); };
            FormClosed += delegate { SavePersistentUiSettings(); };
            HandleDestroyed += delegate { SavePersistentUiSettings(); };
            VisibleChanged += delegate { if (!Visible) SavePersistentUiSettings(); };
            if (ContextMenuStrip != null)
                ContextMenuStrip.Closed += delegate { SavePersistentUiSettings(); };

            Move += delegate { SaveLayoutAfterUserMoveOrResize(); };
            Resize += delegate { SaveLayoutAfterUserMoveOrResize(); };

            _setupButton.Click += delegate { SetupClicked(); };
            _roomButton.Click += async delegate { await ChangeRoomClicked(); };
            _roomTitleBox.Click += async delegate { await ChangeRoomClicked(); };
            _mapButton.Click += delegate { ShowMapWindow(); };
            _connectButton.Click += async delegate { await ConnectClicked(); };
            _disconnectButton.Click += delegate { DisconnectClicked(); };
            _sendButton.Click += async delegate { await CqClicked(); };
            _cqButton.Click += async delegate { await ToCallClicked(); };

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
            _inputFocusTimer.Interval = 60;
            _inputFocusTimer.Tick += delegate
            {
                if (_inputFocusTarget == null || DateTime.UtcNow > _inputFocusUntilUtc)
                {
                    _inputFocusTimer.Stop();
                    _inputFocusTarget = null;
                    return;
                }
                try
                {
                    if (!_inputFocusTarget.IsDisposed && _inputFocusTarget.CanFocus)
                    {
                        ActiveControl = _inputFocusTarget;
                        _inputFocusTarget.Focus();
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

            _airScoutRefreshTimer = new System.Windows.Forms.Timer();
            // Auto-scan one KST station at a time. AirScout normally replies quickly,
            // so a short UI timer lets the whole list populate without flooding UDP.
            _airScoutRefreshTimer.Interval = 250;
            _airScoutRefreshTimer.Tick += delegate
            {
                RunAirScoutAutoScanTick();
                UpdateAirScoutStatusLabel();
            };
            ConfigureAirScoutClient();

            AdjustUserColumns();
            AdjustMessageColumns();
        }

        private ListView CreateMessageListView()
        {
            ListView lv = new ListView();
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
            try
            {
                if (ContextMenuStrip == null) return;
                HookTitleBarMenuPersistence(ContextMenuStrip.Items);
                ContextMenuStrip.Closed += delegate { SavePersistentUiSettings(); };
            }
            catch { }
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
            e.Cancel = true;
            e.NewWidth = _users.Columns[e.ColumnIndex].Width;
        }

        private void AdjustUserColumns()
        {
            if (_users == null || _users.Columns.Count < 6) return;

            // Keep the AirScout result compact: it only displays NOW, Xm, '-'
            // or blank. Give the reclaimed space to the more useful Name column.
            int callW = 90;
            int locW = 80;
            int qtfW = 60;
            int qrbW = 80;
            const int asW = 48;
            int nameW = Math.Max(145,
                _users.ClientSize.Width - callW - locW - qtfW - qrbW - asW - 4);
            int[] widths = new int[] { callW, nameW, locW, qtfW, qrbW, asW };

            for (int i = 0; i < widths.Length; i++)
            {
                if (_users.Columns[i].Width != widths[i]) _users.Columns[i].Width = widths[i];
            }
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
            if (_roomTitleBox != null)
            {
                _roomTitleBox.BackColor = KstColor("List background", SystemColors.Window);
                _roomTitleBox.ForeColor = KstColor("List text", SystemColors.WindowText);
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

            ApplyButtonColors();
        }

        private void ApplyButtonColors()
        {
            Color buttonBack = KstColor("Button background", SystemColors.Control);
            Color buttonFore = KstColor("Button text", SystemColors.ControlText);

            ApplyButtonColors(_setupButton, buttonBack, buttonFore);
            ApplyButtonColors(_roomButton, buttonBack, buttonFore);
            ApplyButtonColors(_mapButton, buttonBack, buttonFore);
            ApplyButtonColors(_connectButton, buttonBack, buttonFore);
            ApplyButtonColors(_disconnectButton, buttonBack, buttonFore);
            ApplyButtonColors(_sendButton, buttonBack, buttonFore);
            ApplyButtonColors(_cqButton, buttonBack, buttonFore);
            ApplyButtonColors(_editMacrosButton, buttonBack, buttonFore);

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

        private void StyleUserItem(ListViewItem item, KstUserInfo user)
        {
            if (item == null) return;
            bool selected = item.Selected;
            bool worked = false;
            if (user != null) worked = IsWorkedBefore(user.Call);

            if (selected)
            {
                ApplyItemStyleToSubItems(item,
                    KstColor("Selected row background", Color.Yellow),
                    KstColor("Selected row text", Color.Black),
                    _boldFont ?? _windowFont);
            }
            else if (worked)
            {
                ApplyItemStyleToSubItems(item,
                    KstColor("In log background", KstColor("Worked/B4 background", Color.Gainsboro)),
                    KstColor("In log text", KstColor("Worked/B4 text", Color.DimGray)),
                    _italicFont ?? _windowFont);
            }
            else
            {
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
            Font f = e.SubItem.Font ?? e.Item.Font ?? _windowFont;
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, f, e.Bounds, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            using (Pen pen = new Pen(SystemColors.ControlDark))
            {
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom);
            }
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
            if (c == null) return;
            _inputFocusTarget = c;
            _inputFocusUntilUtc = DateTime.UtcNow.AddSeconds(3);
            try
            {
                ActiveControl = c;
                c.Focus();
                TextBox tb = c as TextBox;
                if (tb != null) tb.SelectionStart = tb.Text.Length;
            }
            catch { }
            if (_inputFocusTimer != null && !_inputFocusTimer.Enabled) _inputFocusTimer.Start();
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
                RecalculateAllUsers();
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
            KstRoomDialog dlg = new KstRoomDialog(_settings != null ? _settings.Room : 2);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            int newRoom = dlg.Room;
            if (_settings == null) _settings = new KstSettings();
            if (_settings.Room == newRoom)
            {
                ApplySettingsToUi();
                return;
            }

            _settings.Room = newRoom;
            ApplySettingsToUi();
            _settings.Save();
            _userMap.Clear();
            if (_users != null) _users.Items.Clear();
            _messages.Items.Clear();
            if (_threadMessages != null) _threadMessages.Items.Clear();

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

        private void EditMacrosClicked()
        {
            KstMacroDialog dlg = new KstMacroDialog(_settings.Macros);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _settings.Macros = dlg.Macros;
                _settings.Save();
                UpdateStatus("KST macros saved");
            }
        }

        private async Task SendMacroClicked(int index)
        {
            if (_kst == null || !_kst.IsConnected) return;
            if (index < 0 || index >= _settings.Macros.Length) return;
            string call = GetHighlightedCall();
            if (String.IsNullOrWhiteSpace(call))
            {
                UpdateStatus("Highlight a station first before sending a macro.");
                return;
            }
            string template = _settings.Macros[index] ?? "";
            string body = ExpandMacro(template, call).Trim();
            if (body.Length == 0)
            {
                UpdateStatus("Macro " + (index + 1).ToString() + " is empty. Use Edit macros first.");
                return;
            }
            await SendDirectedMessageAsync(call, body);
            UpdateStatus("Sent M" + (index + 1).ToString() + " to " + call.ToUpperInvariant());
        }

        private void ApplySettingsToUi()
        {
            _hostBox.Text = _settings.Host;
            _portBox.Value = Math.Max(_portBox.Minimum, Math.Min(_portBox.Maximum, _settings.Port));
            _roomTitleBox.Text = KstRoomTitles.GetTitle(_settings.Room);
            _userBox.Text = _settings.Callsign;
            _passBox.Text = _settings.Password;
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
                _users.Items.Clear();
                _userMap.Clear();
                SetConnectionUi(false);

                _kst = new TelnetKstClient(_settings.Host, _settings.Port, _settings.Callsign, _settings.Password, _settings.Room);
                _kst.LineReceived += OnKstLineReceived;
                _kst.StatusChanged += OnKstStatusChanged;
                _kst.LoggedIn += OnKstLoggedIn;

                await _kst.ConnectAsync();
                SetConnectionUi(true);
                UpdateStatus("Connected - " + KstRoomTitles.GetTitle(_settings.Room) + " - waiting for ON4KST login prompts");
            }
            catch (Exception ex)
            {
                SetConnectionUi(false);
                UpdateStatus("Connect failed: " + ex.Message);
                if (_mainForm != null) _mainForm.SetMainStatusText("KST connect failed: " + ex.Message);
            }
        }

        private void DisconnectClicked()
        {
            try
            {
                if (_kst != null && _kst.IsConnected) _kst.SendCommandAsync("/QUIT");
            }
            catch { }

            if (_kst != null)
            {
                _kst.Dispose();
                _kst = null;
            }
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
            ClearListSelection(_users);
            ClearListSelection(_messages);
            ClearListSelection(_threadMessages);
            _lastSelectedCall = "";
            RestyleUsers();
            RestyleMessages();
            RestyleThreadMessages();
            RefreshConversationView();
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
            // No inline message box is used. Send/To call open modal compose dialogs
            // so DXLog cannot steal keyboard focus back to the log input line.
        }

        private async Task RefreshUsers()
        {
            if (_kst == null || !_kst.IsConnected) return;
            _refreshingUserList = true;
            foreach (KstUserInfo u in _userMap.Values) u.Dirty = true;
            await _kst.SendCommandAsync("/SH US");
            UpdateStatus("Requested KST user list");
        }

        private void OnKstLoggedIn(object sender, EventArgs e)
        {
            SafeUi(async delegate
            {
                _userRefreshTimer.Start();
                await ApplyOwnProfileToKst(null);
                await RefreshUsers();
            });
        }

        private void OnKstStatusChanged(object sender, string status)
        {
            SafeUi(delegate { UpdateStatus(status); });
        }

        private void OnKstLineReceived(object sender, string line)
        {
            SafeUi(delegate { ApplyKstLine(line); });
        }

        private void ApplyKstLine(string line)
        {
            if (String.IsNullOrWhiteSpace(line)) return;
            KstParsedLine parsed = KstTextParser.Parse(_settings.Callsign, line);
            if (parsed.Type == KstParsedType.User)
            {
                UpsertUser(new KstUserInfo { Call = parsed.Call, Name = parsed.Name, Locator = parsed.Locator, Dirty = false });
                return;
            }

            if (parsed.Type == KstParsedType.Chat || parsed.Type == KstParsedType.DxClusterSpot)
            {
                AddChatMessage(parsed);
                return;
            }

            if (parsed.Type == KstParsedType.Prompt)
            {
                _refreshingUserList = false;
                CleanupUserList();
                return;
            }
        }

        private void CleanupUserList()
        {
            if (!_refreshingUserList) return;
            _refreshingUserList = false;
            List<string> remove = new List<string>();
            foreach (KeyValuePair<string, KstUserInfo> kv in _userMap)
            {
                if (kv.Value.Dirty) remove.Add(kv.Key);
            }
            foreach (string call in remove) RemoveUser(call);
            SortUsers();
        }

        private void AddChatMessage(KstParsedLine msg)
        {
            if (msg == null) return;
            if (!String.IsNullOrWhiteSpace(msg.Call) && !_userMap.ContainsKey(msg.Call))
                UpsertUser(new KstUserInfo { Call = msg.Call, Name = msg.Name, Locator = "", Dirty = false });

            bool worked = IsWorkedBefore(msg.Call);
            msg.Worked = worked;
            bool directToMe = IsDirectToMe(msg);
            ListViewItem item = CreateMessageItem(msg);
            _messages.Items.Add(item);
            string conversationCall = GetOtherPartyForMessage(msg);
            _lastSelectedCall = String.IsNullOrWhiteSpace(conversationCall) ? msg.Call : conversationCall;

            if (_messages.Items.Count > 1500) _messages.Items.RemoveAt(0);
            if (_messages.Items.Count > 0) _messages.EnsureVisible(_messages.Items.Count - 1);
            RefreshConversationView();

            if (directToMe)
            {
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
            KstUserInfo existing;
            if (_userMap.TryGetValue(user.Call, out existing))
            {
                if (String.IsNullOrWhiteSpace(user.Name)) user.Name = existing.Name;
                if (String.IsNullOrWhiteSpace(user.Locator)) user.Locator = existing.Locator;
            }
            _userMap[user.Call] = user;

            ListViewItem item = null;
            foreach (ListViewItem it in _users.Items)
            {
                if (String.Equals(it.Text, user.Call, StringComparison.OrdinalIgnoreCase)) { item = it; break; }
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
                while (item.SubItems.Count < 6) item.SubItems.Add("");
                item.SubItems[5].Text = GetAirScoutDisplay(user.Call);
                item.Tag = user;
                UpdateAirScoutItemToolTip(item);
                StyleUserItem(item, user);
            }
            SortUsers();
            RefreshMapWindow();
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
            RefreshMapWindow();
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
                StyleUserItem(item, user);
            }
            SortUsers();
            RefreshMapWindow();
        }

        private void UsersColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column != 0 && e.Column != 2 && e.Column != 3 && e.Column != 4 && e.Column != 5) return;

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

        private void UpdateUserColumnHeaders()
        {
            if (_users == null || _users.Columns.Count < 6) return;
            string[] headers = new string[] { "Call", "Name", "Loc", "QTF", "QRB", "AS" };
            for (int i = 0; i < headers.Length; i++)
            {
                string suffix = "";
                if (i == _userSortColumn) suffix = _userSortOrder == SortOrder.Ascending ? " ▲" : " ▼";
                _users.Columns[i].Text = headers[i] + suffix;
            }
        }

        private void UsersMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            ListViewItem item = _users.GetItemAt(e.X, e.Y);
            if (item == null) return;
            item.Selected = true;
            ContextMenuStrip menu = MakeCallMenu(item.Text);
            menu.Show(_users, e.Location);
        }

        private void MessagesMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
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
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Put " + call + " into DXLog", null, delegate { PutCallIntoDxLog(call, GetKstLocatorForCall(call)); });
            menu.Items.Add("Message " + call, null, async delegate { await CqCall(call); });
            menu.Items.Add("Copy call", null, delegate { if (!String.IsNullOrWhiteSpace(call)) Clipboard.SetText(call); });
            menu.Items.Add("Send message...", null, async delegate { await SendMessageToCall(call, ""); });
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem showInAirScout = new ToolStripMenuItem("Show path in AirScout");
            showInAirScout.Enabled = CanQueryAirScoutForCall(call);
            showInAirScout.Click += delegate { ShowCallPathInAirScout(call); };
            menu.Items.Add(showInAirScout);
            return menu;
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
            if (String.IsNullOrWhiteSpace(call)) return;
            call = CleanCall(call);
            if (String.IsNullOrWhiteSpace(call)) return;
            kstLocator = NormalizeLocator(kstLocator);

            try
            {
                if (_mainForm == null) _mainForm = (FrmMain)(ParentForm == null ? Owner : ParentForm);
                if (_mainForm == null) throw new InvalidOperationException("DXLog main form not found");

                UCQSO qso = _mainForm.CurrentEntryLine;
                if (qso == null) throw new InvalidOperationException("No active QSO entry line");

                TextBox tb = qso.Controls["txtCallSign"] as TextBox;
                if (tb == null) throw new InvalidOperationException("txtCallSign control not found");

                tb.Text = call;
                tb.SelectionStart = tb.Text.Length;
                tb.Focus();

                // Let DXLog do its normal call lookup first. This may populate the
                // Grid/QRA field from DXLog's own database.
                InvokeDxLogKeyCommand("CHECK_CALL_CLICK", qso.Name, "txtCallSign");

                // If DXLog did not provide a locator, use the QRA/locator from the
                // ON4KST user list. BeginInvoke gives DXLog's lookup code a chance
                // to finish before we decide the QRA field is empty.
                if (IsValidLocator(kstLocator))
                {
                    BeginInvoke(new MethodInvoker(delegate { FillQraFromKstIfMissing(qso, call, kstLocator); }));
                }

                UpdateStatus("Inserted " + call + " into DXLog" + (IsValidLocator(kstLocator) ? " / KST QRA " + kstLocator : ""));
                _mainForm.SetMainStatusText("KST selected " + call);
            }
            catch (Exception ex)
            {
                try { Clipboard.SetText(call); } catch { }
                UpdateStatus("DXLog insert failed, copied instead: " + ex.Message);
            }
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

        private void FillQraFromKstIfMissing(UCQSO qso, string call, string kstLocator)
        {
            if (qso == null || !IsValidLocator(kstLocator)) return;

            // If DXLog already filled any exchange field with a valid locator, leave
            // it alone. DXLog database data takes priority over ON4KST.
            foreach (string name in new string[] { "txtRecInfo", "txtRecInfo2", "txtRecInfo3" })
            {
                TextBox existing = qso.Controls[name] as TextBox;
                if (existing != null && IsValidLocator(existing.Text)) return;
            }

            TextBox target = GetPreferredQraTextBox(qso);
            if (target == null) return;

            string current = (target.Text ?? "").Trim();
            if (current.Length > 0 && IsValidLocator(current)) return;

            target.Text = kstLocator.ToUpperInvariant();
            target.SelectionStart = target.Text.Length;
            target.Focus();
            UpdateStatus("Inserted " + call + " into DXLog and filled QRA from KST: " + kstLocator);
            try { if (_mainForm != null) _mainForm.SetMainStatusText("KST QRA for " + call + " = " + kstLocator); } catch { }
        }

        private TextBox GetPreferredQraTextBox(UCQSO qso)
        {
            // Prefer the exchange field that the current DXLog contest definition
            // declares as GRID. For RSGB VHF NFD this is txtRecInfo.
            string gridControl = GetConfiguredGridControlName();
            if (!String.IsNullOrWhiteSpace(gridControl))
            {
                TextBox configured = qso.Controls[gridControl] as TextBox;
                if (IsUsableQraTarget(configured)) return configured;
            }

            foreach (string name in new string[] { "txtRecInfo", "txtRecInfo2", "txtRecInfo3" })
            {
                TextBox tb = qso.Controls[name] as TextBox;
                if (IsUsableQraTarget(tb)) return tb;
            }
            return null;
        }

        private bool IsUsableQraTarget(TextBox tb)
        {
            if (tb == null) return false;
            if (!tb.Visible || tb.ReadOnly) return false;
            string text = (tb.Text ?? "").Trim();
            // Fill only when the field is blank or not already a locator.
            // This avoids overwriting a locator supplied by the DXLog database.
            return text.Length == 0 || !IsValidLocator(text);
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
                qrb = distance.ToString() + " km";
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
                    RestyleUsers();
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
                RestyleUsers();
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
                if (_messages != null)
                {
                    foreach (ListViewItem item in _messages.Items)
                    {
                        KstParsedLine msg = item.Tag as KstParsedLine;
                        if (msg == null || String.IsNullOrWhiteSpace(msg.Call)) continue;
                        msg.Worked = IsWorkedBefore(msg.Call);
                    }
                }
                if (_threadMessages != null)
                {
                    foreach (ListViewItem item in _threadMessages.Items)
                    {
                        KstParsedLine msg = item.Tag as KstParsedLine;
                        if (msg == null || String.IsNullOrWhiteSpace(msg.Call)) continue;
                        msg.Worked = IsWorkedBefore(msg.Call);
                    }
                }
            }
            catch { }
        }

        private void HandleDxLogFocusChanged()
        {
            SafeUi(delegate
            {
                UpdateStatus("DXLog radio " + (_contestData != null ? _contestData.FocusedRadio.ToString() : "?") + " | KST " + KstRoomTitles.GetTitle(_settings.Room) + " " + (_kst != null && _kst.IsConnected ? "connected" : "not connected"));
                _airScoutResults.Clear();
                RefreshAllAirScoutCells();
                ResetAirScoutAutoScan(true);
                QuerySelectedUserInAirScout(true);
            });
        }

        private void UsersSelectedIndexChanged()
        {
            if (_users != null && _users.SelectedItems.Count > 0)
            {
                _lastSelectedCall = _users.SelectedItems[0].Text;
                QuerySelectedUserInAirScout(true);
            }
            RestyleUsers();
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
                _airScoutResults[call] = result;
                _lastAirScoutReplyUtc = DateTime.UtcNow;
                if (String.Equals(_airScoutPendingAutoCall, call, StringComparison.OrdinalIgnoreCase))
                {
                    _airScoutPendingAutoCall = "";
                    _airScoutPendingAutoSinceUtc = DateTime.MinValue;
                    _airScoutScanCompleted++;
                    if (_airScoutScanCompleted > _airScoutScanTotal) _airScoutScanCompleted = _airScoutScanTotal;
                    if (_airScoutScanQueue.Count == 0) _lastAirScoutFullScanUtc = DateTime.UtcNow;
                }
                UpdateAirScoutRow(call);
                UpdateAirScoutStatusLabel();
                RefreshMapWindow();
            });
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
            _lastAirScoutFullScanUtc = startImmediately ? DateTime.MinValue : DateTime.UtcNow;
        }

        private void BuildAirScoutAutoScanQueue()
        {
            _airScoutScanQueue.Clear();
            _airScoutScanQueuedCalls.Clear();
            _airScoutScanCompleted = 0;
            _airScoutScanTotal = 0;
            _airScoutAutoScanQrg = GetAirScoutFrequency100Hz();

            if (_users == null || _settings == null || !_settings.AirScoutEnabled) return;

            string myCall = CleanCall(_settings.Callsign);
            foreach (ListViewItem item in _users.Items)
            {
                string call = CleanCall(item.Text);
                if (String.IsNullOrWhiteSpace(call) || String.Equals(call, myCall, StringComparison.OrdinalIgnoreCase)) continue;
                if (!CanQueryAirScoutForCall(call)) continue;
                if (_airScoutScanQueuedCalls.Add(call)) _airScoutScanQueue.Enqueue(call);
            }
            _airScoutScanTotal = _airScoutScanQueue.Count;
        }

        private void RunAirScoutAutoScanTick()
        {
            if (_settings == null || !_settings.AirScoutEnabled || _airScout == null || !_airScout.IsListening) return;

            long qrg = GetAirScoutFrequency100Hz();
            if (qrg <= 0) return;
            if (_airScoutAutoScanQrg != 0 && qrg != _airScoutAutoScanQrg)
            {
                // Results are band-specific. Clear the old band immediately and rescan.
                _airScoutResults.Clear();
                RefreshAllAirScoutCells();
                ResetAirScoutAutoScan(true);
            }

            if (!String.IsNullOrWhiteSpace(_airScoutPendingAutoCall))
            {
                // ASNEAREST normally arrives almost immediately. After two seconds,
                // move on so one bad path can never stall the whole KST list.
                if (DateTime.UtcNow - _airScoutPendingAutoSinceUtc < TimeSpan.FromSeconds(2)) return;
                _airScoutPendingAutoCall = "";
                _airScoutPendingAutoSinceUtc = DateTime.MinValue;
                _airScoutScanCompleted++;
                if (_airScoutScanCompleted > _airScoutScanTotal) _airScoutScanCompleted = _airScoutScanTotal;
            }

            if (_airScoutScanQueue.Count == 0)
            {
                if (_airScoutScanTotal > 0 && _airScoutScanCompleted >= _airScoutScanTotal)
                {
                    if (_lastAirScoutFullScanUtc == DateTime.MinValue) _lastAirScoutFullScanUtc = DateTime.UtcNow;
                    // Refresh the complete room periodically. New/changed KST users are
                    // also picked up automatically on the next cycle.
                    if (DateTime.UtcNow - _lastAirScoutFullScanUtc < TimeSpan.FromSeconds(20)) return;
                }
                BuildAirScoutAutoScanQueue();
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
            AirScoutPlaneInfo best = result.GetBestPlane();
            if (best == null) return "-";
            return best.Mins <= 0 ? "NOW" : best.Mins.ToString() + "m";
        }

        private void RefreshAllAirScoutCells()
        {
            if (_users == null) return;
            foreach (ListViewItem item in _users.Items)
            {
                while (item.SubItems.Count < 6) item.SubItems.Add("");
                item.SubItems[5].Text = GetAirScoutDisplay(item.Text);
                UpdateAirScoutItemToolTip(item);
            }
        }

        private void UpdateAirScoutRow(string call)
        {
            if (_users == null) return;
            foreach (ListViewItem item in _users.Items)
            {
                if (!String.Equals(CleanCall(item.Text), CleanCall(call), StringComparison.OrdinalIgnoreCase)) continue;
                while (item.SubItems.Count < 6) item.SubItems.Add("");
                item.SubItems[5].Text = GetAirScoutDisplay(call);
                UpdateAirScoutItemToolTip(item);
                break;
            }
        }

        private void UpdateAirScoutItemToolTip(ListViewItem item)
        {
            if (item == null)
                return;
            AirScoutPathResult result;
            if (!_airScoutResults.TryGetValue(CleanCall(item.Text), out result) || result == null)
            {
                item.ToolTipText = "";
                return;
            }
            AirScoutPlaneInfo best = result.GetBestPlane();
            if (best == null)
            {
                item.ToolTipText = "AirScout: no suitable aircraft reported for " + item.Text;
                return;
            }
            string opportunity = best.Mins <= 0 ? "now" : "in " + best.Mins.ToString() + " min";
            item.ToolTipText = "AirScout " + item.Text + Environment.NewLine +
                "Aircraft: " + best.Call + (String.IsNullOrWhiteSpace(best.Category) ? "" : " (" + best.Category + ")") + Environment.NewLine +
                "Opportunity: " + opportunity + Environment.NewLine +
                "Potential: " + best.Potential.ToString() + Environment.NewLine +
                "Intersection QRB: " + best.IntQRB.ToString() + " km";
        }

        private void SetConnectionUi(bool connected)
        {
            _connectButton.Enabled = !connected;
            _disconnectButton.Enabled = connected;
            _sendButton.Enabled = connected;
            _cqButton.Enabled = connected;
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
            if (_roomButton != null) _roomButton.Enabled = true;
            if (_roomTitleBox != null) _roomTitleBox.Enabled = true;
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
                        _airScoutPlaneById.Clear();
                        foreach (KeyValuePair<string, AirScoutLivePlane> kv in parsed)
                            _airScoutPlaneById[kv.Key] = kv.Value;
                        _lastAirScoutPlaneFetchUtc = DateTime.UtcNow;
                        int unique = new HashSet<AirScoutLivePlane>(_airScoutPlaneById.Values).Count;
                        _airScoutPlaneFeedStatus = unique.ToString() + " live aircraft";
                    }
                    SafeUi(delegate { RefreshMapWindow(); });
                }
                catch (Exception ex)
                {
                    lock (_airScoutPlaneLock)
                    {
                        _lastAirScoutPlaneFetchUtc = DateTime.UtcNow;
                        _airScoutPlaneFeedStatus = "Aircraft feed: " + ex.Message;
                    }
                    SafeUi(delegate { RefreshMapWindow(); });
                }
                finally
                {
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
                if (candidate == null || candidate.Mins >= 30) continue;
                string key = NormalizeAircraftId(candidate.Call);
                AirScoutLivePlane live;
                if (String.IsNullOrWhiteSpace(key) || !liveById.TryGetValue(key, out live) || live == null || !used.Add(live)) continue;
                result.Add(new KstMapAircraft
                {
                    Call = !String.IsNullOrWhiteSpace(candidate.Call) ? candidate.Call.Trim() : live.DisplayName,
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

        private string GetAirScoutPlaneFeedStatus()
        {
            lock (_airScoutPlaneLock) return _airScoutPlaneFeedStatus;
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

        private void RefreshMapWindow()
        {
            try
            {
                if (_mapForm != null && !_mapForm.IsDisposed)
                    _mapForm.RefreshStations();
            }
            catch { }
        }

        private List<KstMapStation> GetMapStationsSnapshot()
        {
            List<KstMapStation> result = new List<KstMapStation>();
            try
            {
                foreach (KstUserInfo user in _userMap.Values)
                {
                    if (user == null || String.IsNullOrWhiteSpace(user.Call) || !IsValidLocator(user.Locator)) continue;
                    string qtf;
                    string qrb;
                    CalculateQtfQrb(user.Locator, out qtf, out qrb);
                    GeoPoint pos = LocatorToPoint(user.Locator);
                    result.Add(new KstMapStation
                    {
                        Call = CleanCall(user.Call),
                        Name = user.Name ?? "",
                        Locator = NormalizeLocator(user.Locator),
                        Lat = pos.Lat,
                        Lon = pos.Lon,
                        Qtf = qtf,
                        Qrb = qrb,
                        Worked = IsWorkedBefore(user.Call),
                        IsSelected = String.Equals(CleanCall(user.Call), CleanCall(_lastSelectedCall), StringComparison.OrdinalIgnoreCase)
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

            PutCallIntoDxLog(station.Call, station.Locator);
            if (turnRotator) TurnRotatorToStation(station.Call, station.Locator);
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
                if (_mainForm == null) _mainForm = (FrmMain)(ParentForm == null ? Owner : ParentForm);
                if (_mainForm == null) return;

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
                        MethodInfo dxLogCtrlF12 = typeof(FrmMain).GetMethod(
                            "turnAntennaToLoggedCallShortPathToolStripMenuItem_Click",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                        if (dxLogCtrlF12 != null)
                        {
                            dxLogCtrlF12.Invoke(_mainForm, new object[] { this, EventArgs.Empty });
                            UpdateStatus("Selected " + cleanCall + " and triggered DXLog Ctrl+F12 rotator command (" + azimuth.ToString() + "°)");
                            return;
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
            public bool IsSelected;
        }

        private sealed class KstMapAircraft
        {
            public string Call;
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
            private readonly Button _refreshButton;
            private readonly Button _zoomInButton;
            private readonly Button _zoomOutButton;
            private readonly Button _zoomResetButton;
            private readonly Label _status;
            private readonly System.Windows.Forms.Timer _refreshTimer;
            private readonly Icon _mapIcon;

            public KstUserMapForm(KstChatBridge owner)
            {
                _owner = owner;
                Text = "KST Users Map";
                StartPosition = FormStartPosition.CenterParent;
                Size = new Size(900, 620);
                MinimumSize = new Size(500, 360);
                Font = owner._windowFont;
                _mapIcon = CreateGlobeIcon();
                if (_mapIcon != null) Icon = _mapIcon;

                TableLayoutPanel layout = new TableLayoutPanel();
                layout.Dock = DockStyle.Fill;
                layout.RowCount = 3;
                layout.ColumnCount = 7;
                layout.Padding = new Padding(6);
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

                _refreshButton = new Button { Text = "Refresh", Dock = DockStyle.Fill };
                _zoomInButton = new Button { Text = "Zoom +", Dock = DockStyle.Fill };
                _zoomOutButton = new Button { Text = "Zoom -", Dock = DockStyle.Fill };
                _zoomResetButton = new Button { Text = "Fit", Dock = DockStyle.Fill };
                _turnRotator = new CheckBox { Text = "Turn rotator on click", Checked = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
                _showAirScout = new CheckBox { Text = "Show AirScout path and aircraft", Checked = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
                _status = new Label { Text = "Click a station to select it in DXLog", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

                _canvas = new KstMapCanvas(owner);
                _canvas.Dock = DockStyle.Fill;
                _canvas.StationClicked += delegate(KstMapStation station) { StationClicked(station); };

                layout.Controls.Add(_refreshButton, 0, 0);
                layout.Controls.Add(_zoomInButton, 1, 0);
                layout.Controls.Add(_zoomOutButton, 2, 0);
                layout.Controls.Add(_zoomResetButton, 3, 0);
                layout.Controls.Add(_turnRotator, 4, 0);
                layout.Controls.Add(_showAirScout, 5, 0);
                Button closeButton = new Button { Text = "Close", Dock = DockStyle.Fill };
                closeButton.Click += delegate { Close(); };
                layout.Controls.Add(closeButton, 6, 0);
                layout.Controls.Add(_canvas, 0, 1); layout.SetColumnSpan(_canvas, 7);
                layout.Controls.Add(_status, 0, 2); layout.SetColumnSpan(_status, 7);
                Controls.Add(layout);

                _refreshButton.Click += delegate { RefreshStations(); };
                _zoomInButton.Click += delegate { _canvas.ZoomIn(); UpdateZoomStatus(); };
                _zoomOutButton.Click += delegate { _canvas.ZoomOut(); UpdateZoomStatus(); };
                _zoomResetButton.Click += delegate { _canvas.ResetZoom(); UpdateZoomStatus(); };
                _showAirScout.CheckedChanged += delegate { RefreshStations(); };
                Shown += delegate { RefreshStations(); };

                _refreshTimer = new System.Windows.Forms.Timer();
                _refreshTimer.Interval = 5000;
                _refreshTimer.Tick += delegate { RefreshStations(); };
                _refreshTimer.Start();
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                if (_refreshTimer != null) _refreshTimer.Stop();
                if (_mapIcon != null) _mapIcon.Dispose();
                base.OnFormClosed(e);
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

            public void RefreshStations()
            {
                try
                {
                    KstMapStation own;
                    _canvas.OwnStation = _owner.TryGetOwnMapPoint(out own) ? own : null;
                    _canvas.Stations = _owner.GetMapStationsSnapshot();
                    _canvas.SelectedStation = null;
                    foreach (KstMapStation station in _canvas.Stations)
                    {
                        if (station != null && station.IsSelected) { _canvas.SelectedStation = station; break; }
                    }

                    if (_showAirScout.Checked)
                    {
                        _owner.BeginRefreshAirScoutLivePlanes(false);
                        _canvas.Aircraft = _owner.GetSelectedAirScoutAircraftSnapshot();
                    }
                    else
                        _canvas.Aircraft = new List<KstMapAircraft>();

                    _canvas.Invalidate();
                    if (_canvas.SelectedStation != null && _showAirScout.Checked)
                        _status.Text = _canvas.SelectedStation.Call + " path - " + _canvas.Aircraft.Count.ToString() + " matched aircraft - " + _owner.GetAirScoutPlaneFeedStatus();
                    else
                        _status.Text = _canvas.Stations.Count.ToString() + " stations with valid locators";
                }
                catch { }
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

            private readonly Dictionary<string, Image> _tileImages = new Dictionary<string, Image>();
            private readonly HashSet<string> _tileDownloads = new HashSet<string>();
            private readonly object _tileLock = new object();

            public List<KstMapStation> Stations = new List<KstMapStation>();
            public KstMapStation OwnStation;
            public KstMapStation SelectedStation;
            public List<KstMapAircraft> Aircraft = new List<KstMapAircraft>();
            public event Action<KstMapStation> StationClicked;

            public KstMapCanvas(KstChatBridge owner)
            {
                _owner = owner;
                DoubleBuffered = true;
                BackColor = Color.FromArgb(218, 235, 243);
                TabStop = true;
                Cursor = Cursors.Hand;
                MouseDown += CanvasMouseDown;
                MouseMove += CanvasMouseMove;
                MouseUp += CanvasMouseUp;
                MouseEnter += delegate { try { Focus(); } catch { } };
                MouseWheel += CanvasMouseWheel;
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
                Invalidate();
            }

            public void ZoomOut()
            {
                _fitPending = false;
                CentreZoomOnHome();
                _tileZoom = Math.Max(MinTileZoom, _tileZoom - 1);
                Invalidate();
            }

            public void ResetZoom()
            {
                _fitPending = true;
                Invalidate();
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
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Rectangle area = ClientRectangle;
                area.Inflate(-10, -10);
                if (area.Width <= 10 || area.Height <= 10) return;

                if (_fitPending)
                {
                    FitStations(area);
                    _fitPending = false;
                }

                using (SolidBrush b = new SolidBrush(Color.FromArgb(218, 235, 243))) g.FillRectangle(b, area);
                DrawOpenStreetMapTiles(g, area);
                DrawGrid(g, area);
                DrawRangeRings(g, area);
                DrawSelectedPath(g, area);
                DrawStations(g, area);
                DrawAircraft(g, area);
                using (Pen border = new Pen(Color.SteelBlue)) g.DrawRectangle(border, area);
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
                        try { if (!IsDisposed) BeginInvoke((Action)(delegate { Invalidate(); })); } catch { }
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
                    DrawLabel(g, f, caption, mid.X + 8, mid.Y + 5, Color.Navy, Color.FromArgb(225, Color.White));
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
                if (Aircraft == null || Aircraft.Count == 0) return;
                using (Font labelFont = new Font(_owner._windowFont.FontFamily, Math.Max(7, _owner._windowFont.Size - 1), FontStyle.Bold))
                {
                    foreach (KstMapAircraft plane in Aircraft)
                    {
                        if (plane == null) continue;
                        PointF pt = Project(area, plane.Lat, plane.Lon);
                        if (pt.X < area.Left - 100 || pt.X > area.Right + 100 || pt.Y < area.Top - 60 || pt.Y > area.Bottom + 60) continue;
                        Color colour = plane.Mins <= 0 ? Color.LimeGreen : (plane.Mins <= 5 ? Color.Orange : Color.Gold);
                        DrawAircraftSymbol(g, pt, plane.Track, colour);
                        string when = plane.Mins <= 0 ? "NOW" : plane.Mins.ToString() + "m";
                        string label = (String.IsNullOrWhiteSpace(plane.Call) ? "AIRCRAFT" : plane.Call.Trim()) + " " + when;
                        if (plane.AltitudeFt > 0) label += " " + Math.Round(plane.AltitudeFt / 1000.0, 0).ToString("0") + "kft";
                        DrawLabel(g, labelFont, label, pt.X + 10, pt.Y - 12, Color.Black, Color.FromArgb(235, colour));
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
                using (Font labelFont = new Font(_owner._windowFont.FontFamily, Math.Max(7, _owner._windowFont.Size - 1), FontStyle.Bold))
                using (Font ownFont = new Font(_owner._windowFont.FontFamily, Math.Max(8, _owner._windowFont.Size), FontStyle.Bold))
                {
                    if (OwnStation != null)
                    {
                        PointF own = Project(area, OwnStation.Lat, OwnStation.Lon);
                        using (SolidBrush b = new SolidBrush(Color.LimeGreen)) g.FillEllipse(b, own.X - 6, own.Y - 6, 12, 12);
                        using (Pen p = new Pen(Color.DarkGreen, 2)) g.DrawEllipse(p, own.X - 6, own.Y - 6, 12, 12);
                        DrawLabel(g, ownFont, OwnStation.Call, own.X + 8, own.Y - 14, Color.DarkGreen, Color.FromArgb(220, Color.White));
                    }

                    foreach (KstMapStation s in Stations)
                    {
                        PointF pt = Project(area, s.Lat, s.Lon);
                        if (pt.X < area.Left - 80 || pt.X > area.Right + 80 || pt.Y < area.Top - 30 || pt.Y > area.Bottom + 30) continue;
                        Color dot = s.Worked ? Color.Gray : (s.IsSelected ? Color.Red : Color.OrangeRed);
                        Color labelBack = s.Worked ? Color.FromArgb(220, Color.Gainsboro) : (s.IsSelected ? Color.FromArgb(235, Color.Yellow) : Color.FromArgb(230, Color.Yellow));
                        Color labelText = s.Worked ? Color.DimGray : Color.Black;
                        using (SolidBrush b = new SolidBrush(dot)) g.FillEllipse(b, pt.X - 4, pt.Y - 4, 8, 8);
                        using (Pen p = new Pen(Color.DarkRed)) g.DrawEllipse(p, pt.X - 4, pt.Y - 4, 8, 8);
                        DrawLabel(g, labelFont, s.Call, pt.X + 6, pt.Y - 10, labelText, labelBack);
                        _hits.Add(new KstMapHit { Station = s, Bounds = new RectangleF(pt.X - 8, pt.Y - 8, 16, 16) });
                    }
                }
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
                if (!_dragging) return;
                int dx = e.X - _dragStart.X;
                int dy = e.Y - _dragStart.Y;
                if (Math.Abs(dx) > 2 || Math.Abs(dy) > 2) _dragMoved = true;
                if (!_dragMoved) return;
                GeoPoint gp = PixelToLatLon(_dragStartCenterX - dx, _dragStartCenterY - dy, _tileZoom);
                _centerLat = gp.Lat;
                _centerLon = gp.Lon;
                _fitPending = false;
                Invalidate();
            }

            private void CanvasMouseUp(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                bool click = _dragging && !_dragMoved;
                _dragging = false;
                Capture = false;
                Cursor = Cursors.Hand;
                if (click)
                {
                    KstMapHit best = FindHit(e.Location);
                    if (best != null && StationClicked != null) StationClicked(best.Station);
                }
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

        private sealed class KstMapHit
        {
            public KstMapStation Station;
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
            int result;

            if (_column == 3 || _column == 4)
                result = CompareNumericWithBlanksLast(av, bv);
            else if (_column == 5)
                result = CompareAirScoutOpportunity(av, bv);
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

        private static int CompareAirScoutOpportunity(string a, string b)
        {
            return AirScoutSortValue(a).CompareTo(AirScoutSortValue(b));
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
                    if (data.IndexOf("WRONG PASSWORD", StringComparison.OrdinalIgnoreCase) >= 0 || data.IndexOf("BAD PASSWORD", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        RaiseStatus("Invalid KST password");
                        Dispose();
                        return;
                    }
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
    }

    internal static class KstRoomTitles
    {
        public static string GetTitle(int room)
        {
            switch (room)
            {
                case 1: return "50 MHz";
                case 2: return "144/432 MHz";
                case 3: return "1296 MHz";
                case 4: return "2.3/3.4 GHz";
                case 5: return "5.7/10 GHz";
                case 6: return "24 GHz and up";
                case 7: return "EME";
                case 8: return "MS";
                case 9: return "144/432 MHz IARU R3";
                case 10: return "2000-630 m";
                case 11: return "WARC 30/17/12 m";
                case 12: return "28 MHz";
                case 13: return "40 MHz";
                default: return "Room " + room.ToString();
            }
        }
    }

    internal sealed class KstSettings
    {
        public string Host = "www.on4kst.info";
        public int Port = 23000;
        public int Room = 2;
        public string Callsign = "";
        public string Password = "";
        public string Name = "";
        public string OwnLocator = "";
        public bool AirScoutEnabled = false;
        public int AirScoutPort = 9872;
        public int AirScoutHttpPort = 9880;
        public string[] Macros = new string[]
        {
            "PSE SKED {FREQ} {MODE}",
            "QRV {FREQ} {MODE}?",
            "I CALL YOU {FREQ} {MODE}",
            "TU 73"
        };
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
                    else if (key == "callsign") s.Callsign = val;
                    else if (key == "password") s.Password = val;
                    else if (key == "name") s.Name = val;
                    else if (key == "locator" || key == "ownlocator" || key == "qthlocator") s.OwnLocator = val;
                    else if (key == "airscoutenabled") { bool b; if (Boolean.TryParse(val, out b)) s.AirScoutEnabled = b; }
                    else if (key == "airscoutport" && Int32.TryParse(val, out n) && n > 0 && n <= 65535) s.AirScoutPort = n;
                    else if (key == "airscouthttpport" && Int32.TryParse(val, out n) && n > 0 && n <= 65535) s.AirScoutHttpPort = n;
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
                lines.Add("callsign=" + Callsign);
                lines.Add("password=" + Password);
                lines.Add("name=" + Name);
                lines.Add("locator=" + OwnLocator);
                lines.Add("airscoutenabled=" + AirScoutEnabled.ToString());
                lines.Add("airscoutport=" + AirScoutPort.ToString());
                lines.Add("airscouthttpport=" + AirScoutHttpPort.ToString());
                lines.Add("macro1=" + (Macros != null && Macros.Length > 0 ? Macros[0] : ""));
                lines.Add("macro2=" + (Macros != null && Macros.Length > 1 ? Macros[1] : ""));
                lines.Add("macro3=" + (Macros != null && Macros.Length > 2 ? Macros[2] : ""));
                lines.Add("macro4=" + (Macros != null && Macros.Length > 3 ? Macros[3] : ""));
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
            return new KstSettings { Host = Host, Port = Port, Room = Room, Callsign = Callsign, Password = Password, Name = Name, OwnLocator = OwnLocator, AirScoutEnabled = AirScoutEnabled, AirScoutPort = AirScoutPort, AirScoutHttpPort = AirScoutHttpPort, Macros = m, WindowX = WindowX, WindowY = WindowY, WindowW = WindowW, WindowH = WindowH, TitleBarColor = TitleBarColor, ColorValues = colors };
        }
    }

    internal sealed class KstRoomDialog : Form
    {
        private NumericUpDown _room;
        private Label _roomTitle;
        public int Room { get; private set; }

        public KstRoomDialog(int currentRoom)
        {
            Room = currentRoom <= 0 ? 2 : currentRoom;
            Text = "KST room";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(420, 135);
            ShowInTaskbar = false;

            TableLayoutPanel p = new TableLayoutPanel();
            p.Dock = DockStyle.Top;
            p.Padding = new Padding(12, 12, 12, 0);
            p.ColumnCount = 3;
            p.RowCount = 1;
            p.Height = 56;
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _room = new NumericUpDown { Minimum = 1, Maximum = 99, Value = Room, Dock = DockStyle.Left, Width = 90 };
            _roomTitle = new Label { Text = KstRoomTitles.GetTitle(Room), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            _room.ValueChanged += delegate { _roomTitle.Text = KstRoomTitles.GetTitle((int)_room.Value); };

            p.Controls.Add(new Label { Text = "Room", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, 0);
            p.Controls.Add(_room, 1, 0);
            p.Controls.Add(_roomTitle, 2, 0);

            Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90, Height = 28 };
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Height = 28 };
            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                Padding = new Padding(0, 8, 12, 0),
                FlowDirection = FlowDirection.RightToLeft
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);

            Controls.Add(buttons);
            Controls.Add(p);
            AcceptButton = ok;
            CancelButton = cancel;

            ok.Click += delegate { Room = (int)_room.Value; };
        }
    }

    internal sealed class KstSetupDialog : Form
    {
        private TextBox _host;
        private NumericUpDown _port;
        private NumericUpDown _room;
        private TextBox _call;
        private TextBox _pass;
        private TextBox _name;
        private TextBox _locator;
        private CheckBox _airScoutEnabled;
        private NumericUpDown _airScoutPort;
        private NumericUpDown _airScoutHttpPort;
        public KstSettings Settings { get; private set; }

        public KstSetupDialog(KstSettings settings)
        {
            Settings = settings.Clone();
            Text = "KST setup";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(620, 425);
            ShowInTaskbar = false;

            TableLayoutPanel p = new TableLayoutPanel();
            p.Dock = DockStyle.Top;
            p.Padding = new Padding(12, 12, 12, 0);
            p.ColumnCount = 2;
            p.RowCount = 8;
            p.Height = 326;
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 8; i++) p.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            _host = new TextBox { Text = Settings.Host, Dock = DockStyle.Fill };
            _port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = Settings.Port, Dock = DockStyle.Left, Width = 100 };
            _room = new NumericUpDown { Minimum = 1, Maximum = 99, Value = Settings.Room, Dock = DockStyle.Left, Width = 100 };
            Label roomTitle = new Label { Text = KstRoomTitles.GetTitle(Settings.Room), AutoSize = false, Width = 360, Height = 36, Margin = new Padding(8, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            _room.ValueChanged += delegate { roomTitle.Text = KstRoomTitles.GetTitle((int)_room.Value); };
            _call = new TextBox { Text = Settings.Callsign, Dock = DockStyle.Fill };
            _pass = new TextBox { Text = Settings.Password, UseSystemPasswordChar = true, Dock = DockStyle.Fill };
            _name = new TextBox { Text = Settings.Name, Dock = DockStyle.Fill };
            _locator = new TextBox { Text = Settings.OwnLocator, Dock = DockStyle.Left, Width = 120 };
            _airScoutEnabled = new CheckBox { Text = "Enable AirScout UDP integration", Checked = Settings.AirScoutEnabled, AutoSize = true, Margin = new Padding(0, 8, 18, 0) };
            _airScoutPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = Settings.AirScoutPort > 0 ? Settings.AirScoutPort : 9872, Width = 78, Margin = new Padding(4, 4, 8, 0) };
            _airScoutHttpPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = Settings.AirScoutHttpPort > 0 ? Settings.AirScoutHttpPort : 9880, Width = 78, Margin = new Padding(4, 4, 0, 0) };

            AddRow(p, 0, "Host", _host);
            AddRow(p, 1, "Port", _port);
            FlowLayoutPanel roomPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoScroll = false, Height = 40, Padding = new Padding(0, 0, 0, 0), Margin = new Padding(0) };
            roomPanel.Controls.Add(_room);
            roomPanel.Controls.Add(roomTitle);
            AddRow(p, 2, "Room", roomPanel);
            AddRow(p, 3, "User / call", _call);
            AddRow(p, 4, "Password", _pass);
            AddRow(p, 5, "Name", _name);
            AddRow(p, 6, "QTH locator", _locator);
            FlowLayoutPanel airScoutPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoScroll = false, Height = 40, Padding = new Padding(0), Margin = new Padding(0) };
            airScoutPanel.Controls.Add(_airScoutEnabled);
            airScoutPanel.Controls.Add(new Label { Text = "UDP", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
            airScoutPanel.Controls.Add(_airScoutPort);
            airScoutPanel.Controls.Add(new Label { Text = "HTTP", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
            airScoutPanel.Controls.Add(_airScoutHttpPort);
            AddRow(p, 7, "AirScout", airScoutPanel);

            Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90, Height = 28 };
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Height = 28 };
            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                Padding = new Padding(0, 8, 12, 0),
                FlowDirection = FlowDirection.RightToLeft
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);

            Controls.Add(buttons);
            Controls.Add(p);
            AcceptButton = ok;
            CancelButton = cancel;

            ok.Click += delegate
            {
                Settings.Host = _host.Text.Trim();
                Settings.Port = (int)_port.Value;
                Settings.Room = (int)_room.Value;
                Settings.Callsign = _call.Text.Trim().ToUpperInvariant();
                Settings.Password = _pass.Text;
                Settings.Name = _name.Text.Trim();
                Settings.OwnLocator = _locator.Text.Trim().ToUpperInvariant();
                Settings.AirScoutEnabled = _airScoutEnabled.Checked;
                Settings.AirScoutPort = (int)_airScoutPort.Value;
                Settings.AirScoutHttpPort = (int)_airScoutHttpPort.Value;
            };

            Shown += delegate
            {
                _call.Focus();
                _call.SelectionStart = _call.Text.Length;
            };
        }

        private static void AddRow(TableLayoutPanel p, int row, string label, Control c)
        {
            p.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, row);
            p.Controls.Add(c, 1, row);
        }
    }

    internal sealed class KstMacroDialog : Form
    {
        private readonly TextBox[] _boxes = new TextBox[4];
        public string[] Macros { get; private set; }

        public KstMacroDialog(string[] macros)
        {
            Text = "KST macros";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(720, 285);
            ShowInTaskbar = false;

            Macros = new string[] { "", "", "", "" };
            if (macros != null)
            {
                for (int i = 0; i < Math.Min(4, macros.Length); i++) Macros[i] = macros[i] ?? "";
            }

            TableLayoutPanel p = new TableLayoutPanel();
            p.Dock = DockStyle.Top;
            p.Padding = new Padding(12, 12, 12, 0);
            p.ColumnCount = 2;
            p.RowCount = 5;
            p.Height = 215;
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) p.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));

            for (int i = 0; i < 4; i++)
            {
                _boxes[i] = new TextBox { Text = Macros[i], Dock = DockStyle.Fill };
                p.Controls.Add(new Label { Text = "M" + (i + 1).ToString(), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, i);
                p.Controls.Add(_boxes[i], 1, i);
            }

            Label help = new Label
            {
                Text = "Each macro is sent as a directed message to the highlighted station. Tokens: {CALL}, {MYCALL}, {FREQ}, {BAND}, {MODE}",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false
            };
            p.Controls.Add(help, 0, 4); p.SetColumnSpan(help, 2);

            Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90, Height = 28 };
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Height = 28 };
            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                Padding = new Padding(0, 8, 12, 0),
                FlowDirection = FlowDirection.RightToLeft
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);

            Controls.Add(buttons);
            Controls.Add(p);
            AcceptButton = ok;
            CancelButton = cancel;
            ok.Click += delegate
            {
                for (int i = 0; i < 4; i++) Macros[i] = _boxes[i].Text;
            };
            Shown += delegate { _boxes[0].Focus(); _boxes[0].SelectionStart = _boxes[0].Text.Length; };
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
